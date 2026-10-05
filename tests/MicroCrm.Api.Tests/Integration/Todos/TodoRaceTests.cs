using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using MicroCrm.Api.Data;
using MicroCrm.Api.Tests.Integration.Infrastructure;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace MicroCrm.Api.Tests.Integration.Todos;

// Races between writing a to-do and deleting it (spec 003, T-14). A delete between the handler's load and its
// save must become 404 (DbUpdateConcurrencyException), never a 500, and must never re-create the row.
public sealed class TodoRaceTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Deletes the to-do through a separate connection, once, in the middle of the handler's SaveChanges:
    // after the tracked load and before EF's UPDATE reaches the store.
    private sealed class DeleteTodoOnceInterceptor(string connectionString, Guid todoId) : SaveChangesInterceptor
    {
        private int _fired;

        public int Fired => Volatile.Read(ref _fired);

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
                command.CommandText = "DELETE FROM Todos WHERE lower(Id) = $id";
                command.Parameters.AddWithValue("$id", todoId.ToString().ToLowerInvariant());
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }
    }

    private WebApplicationFactory<Program> Raced(IInterceptor interceptor) =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(interceptor))));

    private static async Task<Guid> NewTodoAsync(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync("/api/todos", new { title, notes = "Original notes" }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> NewDoneTodoAsync(HttpClient client, string title)
    {
        var id = await NewTodoAsync(client, title);
        var response = await client.PostAsync($"/api/todos/{id}/complete", null, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return id;
    }

    private Task<long> RowCountAsync(Guid id) =>
        TodoStoreTests.ScalarAsync(
            factory.ConnectionString,
            "SELECT COUNT(*) FROM Todos WHERE lower(Id) = $id",
            ("$id", id.ToString().ToLowerInvariant()));

    private async Task AssertDeletedAndStaysDeletedAsync(HttpClient seed, Guid id)
    {
        Assert.Equal(0, await RowCountAsync(id));
        await ProblemAssert.IsProblemAsync(await seed.GetAsync($"/api/todos/{id}", Ct), 404);
    }

    [Fact]
    public async Task UpdateTodo_DeletedBetweenLoadAndSave_Returns404AndStaysDeleted_AC070()
    {
        using var seed = factory.CreateClient();
        var todo = await NewTodoAsync(seed, "Doomed update");
        var interceptor = new DeleteTodoOnceInterceptor(factory.ConnectionString, todo);
        using var raced = Raced(interceptor);
        using var client = raced.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/todos/{todo}", new { title = "Changed title" }, Ct);

        Assert.Equal(1, interceptor.Fired);
        using var problem = await ProblemAssert.IsProblemAsync(response, 404);
        await AssertDeletedAndStaysDeletedAsync(seed, todo);
    }

    [Fact]
    public async Task CompleteTodo_DeletedBetweenLoadAndSave_Returns404AndStaysDeleted_AC070()
    {
        using var seed = factory.CreateClient();
        var todo = await NewTodoAsync(seed, "Doomed complete");
        var interceptor = new DeleteTodoOnceInterceptor(factory.ConnectionString, todo);
        using var raced = Raced(interceptor);
        using var client = raced.CreateClient();

        var response = await client.PostAsync($"/api/todos/{todo}/complete", null, Ct);

        Assert.Equal(1, interceptor.Fired);
        using var problem = await ProblemAssert.IsProblemAsync(response, 404);
        await AssertDeletedAndStaysDeletedAsync(seed, todo);
    }

    [Fact]
    public async Task ReopenTodo_DeletedBetweenLoadAndSave_Returns404AndStaysDeleted_AC070()
    {
        using var seed = factory.CreateClient();
        var todo = await NewDoneTodoAsync(seed, "Doomed reopen");
        var interceptor = new DeleteTodoOnceInterceptor(factory.ConnectionString, todo);
        using var raced = Raced(interceptor);
        using var client = raced.CreateClient();

        var response = await client.PostAsync($"/api/todos/{todo}/reopen", null, Ct);

        Assert.Equal(1, interceptor.Fired);
        using var problem = await ProblemAssert.IsProblemAsync(response, 404);
        await AssertDeletedAndStaysDeletedAsync(seed, todo);
    }

    // Guard for the T-09 should-fix (AC-071): DELETE is a single ExecuteDeleteAsync and never calls SaveChanges.
    // A load-then-remove handler would reach SavingChangesAsync, the interceptor would delete the row first, and
    // the remove would fail with DbUpdateConcurrencyException (500). The interceptor must never fire.
    [Fact]
    public async Task DeleteTodo_NeverLoadsThenRemoves_AC071()
    {
        using var seed = factory.CreateClient();
        var todo = await NewTodoAsync(seed, "Deleted directly");
        var interceptor = new DeleteTodoOnceInterceptor(factory.ConnectionString, todo);
        using var raced = Raced(interceptor);
        using var client = raced.CreateClient();

        var response = await client.DeleteAsync($"/api/todos/{todo}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, interceptor.Fired);
        await AssertDeletedAndStaysDeletedAsync(seed, todo);
    }

    // Runs every send behind one gate so the requests start together. Timing decides the interleaving.
    private async Task<HttpResponseMessage[]> RunGatedAsync(List<Func<HttpClient, Task<HttpResponseMessage>>> sends)
    {
        var clients = sends.Select(_ => factory.CreateClient()).ToArray();
        try
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ready = 0;
            var tasks = sends.Select((send, i) => Task.Run(async () =>
            {
                if (Interlocked.Increment(ref ready) == clients.Length)
                {
                    allReady.SetResult();
                }

                await gate.Task;
                return await send(clients[i]);
            }, Ct)).ToArray();

            await allReady.Task.WaitAsync(Ct);
            gate.SetResult();
            return await Task.WhenAll(tasks);
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    // Guard (timing-dependent; the assertions hold for every interleaving).
    [Fact]
    public async Task WritesAndDelete_Concurrent_ReturnDocumentedCodes_AC070()
    {
        const int todos = 12;
        using var seed = factory.CreateClient();
        var targets = new List<(Guid Id, int Kind)>();
        for (var i = 0; i < todos; i++)
        {
            var kind = i % 3; // 0 = PUT, 1 = complete, 2 = reopen
            var id = kind == 2 ? await NewDoneTodoAsync(seed, $"Racing {i}") : await NewTodoAsync(seed, $"Racing {i}");
            targets.Add((id, kind));
        }

        var sends = new List<Func<HttpClient, Task<HttpResponseMessage>>>();
        foreach (var (id, kind) in targets)
        {
            sends.Add(kind switch
            {
                0 => c => c.PutAsJsonAsync($"/api/todos/{id}", new { title = "Racing update" }, Ct),
                1 => c => c.PostAsync($"/api/todos/{id}/complete", null, Ct),
                _ => c => c.PostAsync($"/api/todos/{id}/reopen", null, Ct),
            });
            sends.Add(c => c.DeleteAsync($"/api/todos/{id}", Ct));
        }

        var results = await RunGatedAsync(sends);

        for (var i = 0; i < todos; i++)
        {
            var (id, kind) = targets[i];
            var write = results[2 * i];
            var delete = results[(2 * i) + 1];
            var allowed = kind == 0
                ? new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.NotFound }
                : [HttpStatusCode.OK, HttpStatusCode.NotFound];
            Assert.True(allowed.Contains(write.StatusCode), $"Write kind {kind} returned {(int)write.StatusCode}");
            Assert.True(
                delete.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound,
                $"DELETE returned {(int)delete.StatusCode}");

            if (delete.StatusCode == HttpStatusCode.NoContent)
            {
                await ProblemAssert.IsProblemAsync(await seed.GetAsync($"/api/todos/{id}", Ct), 404);
                Assert.Equal(0, await RowCountAsync(id));
            }
        }
    }

    // Guard (timing-dependent; the invariants hold for every order).
    [Fact]
    public async Task CompleteAndReopen_Concurrent_AllOkAndStateConsistent_AC069()
    {
        const int todos = 6;
        const int pairs = 3;
        using var seed = factory.CreateClient();
        var ids = new List<Guid>();
        for (var i = 0; i < todos; i++)
        {
            ids.Add(await NewTodoAsync(seed, $"Toggled {i}"));
        }

        var sends = new List<Func<HttpClient, Task<HttpResponseMessage>>>();
        foreach (var id in ids)
        {
            for (var k = 0; k < pairs; k++)
            {
                sends.Add(c => c.PostAsync($"/api/todos/{id}/complete", null, Ct));
                sends.Add(c => c.PostAsync($"/api/todos/{id}/reopen", null, Ct));
            }
        }

        var results = await RunGatedAsync(sends);

        Assert.All(results, r => Assert.True(r.StatusCode == HttpStatusCode.OK, $"Returned {(int)r.StatusCode}"));
        foreach (var id in ids)
        {
            var response = await seed.GetAsync($"/api/todos/{id}", Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
            var isDone = doc.RootElement.GetProperty("isDone").GetBoolean();
            var completedAt = doc.RootElement.GetProperty("completedAt");
            Assert.Equal(isDone, completedAt.ValueKind != JsonValueKind.Null);
        }

        Assert.Equal(
            0,
            await TodoStoreTests.ScalarAsync(
                factory.ConnectionString,
                "SELECT COUNT(*) FROM Todos WHERE (IsDone = 1) <> (CompletedAt IS NOT NULL)"));
        Assert.Equal(todos, await TodoStoreTests.ScalarAsync(factory.ConnectionString, "SELECT COUNT(*) FROM Todos"));
    }
}
