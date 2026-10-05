using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

using Microsoft.Data.Sqlite;

namespace MicroCrm.Api.Tests.Integration.Contacts;

public sealed class DeleteContactTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task<Guid> CreateAsync(HttpClient client, string first, string? last, string? email)
    {
        var response = await client.PostAsJsonAsync("/api/contacts", new { firstName = first, lastName = last, email }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<(JsonElement[] Items, int Total)> ListAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync($"/api/contacts{query}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return (doc.RootElement.GetProperty("items").EnumerateArray().ToArray(), doc.RootElement.GetProperty("totalCount").GetInt32());
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

    [Fact]
    public async Task DeleteContact_Existing_Returns204WithEmptyBody_AC026()
    {
        using var client = factory.CreateClient();
        var id = await CreateAsync(client, "Gone", "Soon", "gone.ac026@example.com");

        var response = await client.DeleteAsync($"/api/contacts/{id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task DeleteContact_ThenGet_Returns404OnNewConnections_AC027()
    {
        using var client = factory.CreateClient();
        var id = await CreateAsync(client, "Gone", "Soon", "gone.ac027@example.com");
        Assert.Equal(1, await RowCountAsync(id));

        var deleted = await client.DeleteAsync($"/api/contacts/{id}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var reader = factory.CreateClient();
        var get = await reader.GetAsync($"/api/contacts/{id}", Ct);
        using var problem = await ProblemAssert.IsProblemAsync(get, 404);
        Assert.Equal(0, await RowCountAsync(id));
    }

    [Fact]
    public async Task DeleteContact_ExcludedFromListAndSearch_TotalCountDrops_AC028()
    {
        using var client = factory.CreateClient();
        var doomed = await CreateAsync(client, "Doomed", "Zebra", "doomed.ac028@example.com");
        await CreateAsync(client, "Stays", "Alpha", "stays.ac028@example.com");
        Assert.Equal(2, (await ListAsync(client)).Total);
        Assert.Single((await ListAsync(client, "?search=doomed.ac028")).Items);

        var deleted = await client.DeleteAsync($"/api/contacts/{doomed}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var reader = factory.CreateClient();
        var (items, total) = await ListAsync(reader);
        Assert.Equal(1, total);
        Assert.DoesNotContain(items, i => i.GetProperty("id").GetGuid() == doomed);
        var (searchItems, searchTotal) = await ListAsync(reader, "?search=doomed.ac028");
        Assert.Empty(searchItems);
        Assert.Equal(0, searchTotal);
    }

    [Fact]
    public async Task DeleteContact_EmailCanBeReusedByCreateOrUpdate_AC029()
    {
        using var client = factory.CreateClient();
        var first = await CreateAsync(client, "First", null, "reuse.first.ac029@example.com");
        var second = await CreateAsync(client, "Second", null, "reuse.second.ac029@example.com");
        var other = await CreateAsync(client, "Other", null, "reuse.other.ac029@example.com");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/contacts/{first}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/contacts/{second}", Ct)).StatusCode);

        var created = await client.PostAsJsonAsync(
            "/api/contacts",
            new { firstName = "Again", email = "  REUSE.First.AC029@Example.com " },
            Ct);
        var updated = await client.PutAsJsonAsync(
            $"/api/contacts/{other}",
            new { firstName = "Other", email = "Reuse.Second.ac029@example.COM" },
            Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
    }

    [Fact]
    public async Task DeleteContact_LeavesOtherContactsUnchanged_AC032()
    {
        using var client = factory.CreateClient();
        var doomed = await CreateAsync(client, "Doomed", "Zed", "doomed.ac032@example.com");
        await CreateAsync(client, "Keep", "One", "one.ac032@example.com");
        await CreateAsync(client, "Keep", "Two", null);
        await CreateAsync(client, "Keep", "Three", "three.ac032@example.com");
        var before = (await ListAsync(client)).Items
            .Where(i => i.GetProperty("id").GetGuid() != doomed)
            .ToDictionary(i => i.GetProperty("id").GetGuid(), i => i.GetRawText());
        Assert.Equal(3, before.Count);
        factory.Time.Advance(TimeSpan.FromMinutes(10));

        var deleted = await client.DeleteAsync($"/api/contacts/{doomed}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var after = (await ListAsync(client)).Items
            .ToDictionary(i => i.GetProperty("id").GetGuid(), i => i.GetRawText());
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task DeleteContact_UnknownOrAlreadyDeleted_Returns404Problem_AC030()
    {
        using var client = factory.CreateClient();
        var id = await CreateAsync(client, "Once", null, "once.ac030@example.com");

        var unknown = await client.DeleteAsync($"/api/contacts/{Guid.CreateVersion7()}", Ct);
        using var unknownProblem = await ProblemAssert.IsProblemAsync(unknown, 404);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/contacts/{id}", Ct)).StatusCode);
        var again = await client.DeleteAsync($"/api/contacts/{id}", Ct);
        using var againProblem = await ProblemAssert.IsProblemAsync(again, 404);
    }

    // Guard: no route matches a non-GUID id, so status code pages already give 404 problem+json.
    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("123")]
    public async Task DeleteContact_NonGuidId_Returns404AndDeletesNothing_AC031(string id)
    {
        using var client = factory.CreateClient();
        await CreateAsync(client, "Survivor", null, $"survivor.ac031.{id}@example.com");
        var before = (await ListAsync(client)).Total;

        var response = await client.DeleteAsync($"/api/contacts/{id}", Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 404);
        Assert.Equal(before, (await ListAsync(client)).Total);
    }

    // Guard: the PUT 404 branch exists from T-04.
    [Fact]
    public async Task UpdateContact_AfterDelete_Returns404AndDoesNotRecreate_AC033()
    {
        using var client = factory.CreateClient();
        var id = await CreateAsync(client, "Deleted", null, "deleted.ac033@example.com");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/contacts/{id}", Ct)).StatusCode);

        var put = await client.PutAsJsonAsync($"/api/contacts/{id}", new { firstName = "Back", email = "back.ac033@example.com" }, Ct);

        using var problem = await ProblemAssert.IsProblemAsync(put, 404);
        using var reader = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/api/contacts/{id}", Ct)).StatusCode);
        Assert.Equal(0, await RowCountAsync(id));
        Assert.Equal(0, (await ListAsync(reader)).Total);
    }

    [Fact]
    public async Task DeleteContact_ConcurrentSameId_ExactlyOne204Rest404_AC036()
    {
        const int requests = 10;
        using var seedClient = factory.CreateClient();
        var id = await CreateAsync(seedClient, "Race", null, "race.ac036@example.com");
        var clients = Enumerable.Range(0, requests).Select(_ => factory.CreateClient()).ToArray();
        try
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ready = 0;

            var tasks = clients.Select(client => Task.Run(async () =>
            {
                if (Interlocked.Increment(ref ready) == requests)
                {
                    allReady.SetResult();
                }

                await gate.Task;
                return await client.DeleteAsync($"/api/contacts/{id}", Ct);
            }, Ct)).ToArray();

            await allReady.Task.WaitAsync(Ct);
            gate.SetResult();
            var responses = await Task.WhenAll(tasks);

            Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
            var losers = responses.Where(r => r.StatusCode == HttpStatusCode.NotFound).ToArray();
            Assert.Equal(requests - 1, losers.Length);
            foreach (var loser in losers)
            {
                using var problem = await ProblemAssert.IsProblemAsync(loser, 404);
            }

            Assert.Equal(0, await RowCountAsync(id));
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
