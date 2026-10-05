using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using MicroCrm.Api.Data;
using MicroCrm.Api.Tests.Integration.Infrastructure;

using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace MicroCrm.Api.Tests.Integration.Contacts;

public sealed class UpdateDeleteRaceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> CreateAsync(HttpClient client, string first, string email)
    {
        var response = await client.PostAsJsonAsync("/api/contacts", new { firstName = first, email }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<long> RowCountAsync(Guid id)
    {
        await using var connection = new SqliteConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Contacts WHERE lower(Id) = $id";
        command.Parameters.AddWithValue("$id", id.ToString().ToLowerInvariant());
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    // Deletes the target row through a separate connection, once, after the handler loaded the entity and
    // before EF issues its UPDATE: forces the "deleted between load and save" interleaving.
    private sealed class DeleteOnceInterceptor(string connectionString, Guid id) : SaveChangesInterceptor
    {
        private int _fired;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0)
            {
                await using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM Contacts WHERE lower(Id) = $id";
                command.Parameters.AddWithValue("$id", id.ToString().ToLowerInvariant());
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }
    }

    [Fact]
    public async Task UpdateContact_DeletedBetweenLoadAndSave_Returns404AndStaysDeleted_AC035()
    {
        using var seedClient = factory.CreateClient();
        var id = await CreateAsync(seedClient, "Doomed", "doomed.ac035@example.com");
        var interceptor = new DeleteOnceInterceptor(factory.ConnectionString, id);
        using var raced = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(interceptor))));
        using var client = raced.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/contacts/{id}",
            new { firstName = "Updated", email = "updated.ac035@example.com" },
            Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 404);
        using var reader = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/api/contacts/{id}", Ct)).StatusCode);
        Assert.Equal(0, await RowCountAsync(id));
    }

    // Timing-dependent guard: may pass at RED if no interleaving hits the window. Invariants hold for any order.
    [Fact]
    public async Task UpdateAndDelete_Concurrent_ReturnDocumentedCodes_AC035()
    {
        const int contacts = 10;
        using var seedClient = factory.CreateClient();
        var ids = new List<Guid>();
        for (var i = 0; i < contacts; i++)
        {
            ids.Add(await CreateAsync(seedClient, $"Pair{i}", $"pair{i}.ac035@example.com"));
        }

        var clients = Enumerable.Range(0, contacts * 2).Select(_ => factory.CreateClient()).ToArray();
        try
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ready = 0;

            Task<HttpResponseMessage> Start(int slot, Func<HttpClient, Task<HttpResponseMessage>> send) => Task.Run(async () =>
            {
                if (Interlocked.Increment(ref ready) == clients.Length)
                {
                    allReady.SetResult();
                }

                await gate.Task;
                return await send(clients[slot]);
            }, Ct);

            var puts = ids.Select((id, i) => Start(i * 2, c => c.PutAsJsonAsync(
                $"/api/contacts/{id}",
                new { firstName = $"Updated{i}", email = $"updated{i}.ac035@example.com" },
                Ct))).ToArray();
            var deletes = ids.Select((id, i) => Start((i * 2) + 1, c => c.DeleteAsync($"/api/contacts/{id}", Ct))).ToArray();

            await allReady.Task.WaitAsync(Ct);
            gate.SetResult();
            var putResults = await Task.WhenAll(puts);
            var deleteResults = await Task.WhenAll(deletes);

            Assert.All(putResults, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound, $"PUT returned {(int)r.StatusCode}"));
            Assert.All(deleteResults, r => Assert.True(r.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound, $"DELETE returned {(int)r.StatusCode}"));

            using var reader = factory.CreateClient();
            for (var i = 0; i < contacts; i++)
            {
                if (deleteResults[i].StatusCode == HttpStatusCode.NoContent)
                {
                    Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/api/contacts/{ids[i]}", Ct)).StatusCode);
                }
            }
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }
}
