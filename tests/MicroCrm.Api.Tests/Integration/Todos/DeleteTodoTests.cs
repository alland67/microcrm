using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

namespace MicroCrm.Api.Tests.Integration.Todos;

// Delete a to-do (spec 003, T-09). Each test uses its own data, so the shared class fixture needs no reset.
public sealed class DeleteTodoTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> CreateTodoAsync(HttpClient client, string title, Guid? contactId = null)
    {
        var response = await client.PostAsJsonAsync(
            "/api/todos", new { title, notes = "Some notes", dueDate = "2026-10-05", contactId }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<string> GetBodyAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private async Task<long> RowCountAsync(Guid id) =>
        await TodoStoreTests.ScalarAsync(
            factory.ConnectionString, "SELECT COUNT(*) FROM Todos WHERE lower(Id) = $id", ("$id", id.ToString().ToLowerInvariant()));

    private async Task<long> TodoCountAsync() =>
        await TodoStoreTests.ScalarAsync(factory.ConnectionString, "SELECT COUNT(*) FROM Todos");

    [Fact]
    public async Task DeleteTodo_Existing_Returns204WithEmptyBody_AC038()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var id = await CreateTodoAsync(client, "Delete me");

        var response = await client.DeleteAsync($"/api/todos/{id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task DeleteTodo_ThenGet_Returns404OnNewConnections_AC038()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var id = await CreateTodoAsync(client, "Gone soon");
        Assert.Equal(1, await RowCountAsync(id));

        var deleted = await client.DeleteAsync($"/api/todos/{id}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var reader = factory.CreateClient();
        var get = await reader.GetAsync($"/api/todos/{id}", Ct);
        using var problem = await ProblemAssert.IsProblemAsync(get, 404);
        Assert.Equal(0, await RowCountAsync(id));
    }

    [Fact]
    public async Task DeleteTodo_UnknownOrAlreadyDeleted_Returns404Problem_AC039()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var id = await CreateTodoAsync(client, "Once");

        var unknown = await client.DeleteAsync($"/api/todos/{Guid.CreateVersion7()}", Ct);
        using var unknownProblem = await ProblemAssert.IsProblemAsync(unknown, 404);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/todos/{id}", Ct)).StatusCode);
        var again = await client.DeleteAsync($"/api/todos/{id}", Ct);
        using var againProblem = await ProblemAssert.IsProblemAsync(again, 404);
    }

    // Guard: no route matches a non-GUID id, so status code pages already give 404 problem+json.
    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("123")]
    public async Task DeleteTodo_NonGuidId_Returns404AndDeletesNothing_AC039(string id)
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var survivor = await CreateTodoAsync(client, $"Survivor {id}");
        var before = await TodoCountAsync();

        var response = await client.DeleteAsync($"/api/todos/{id}", Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 404);
        Assert.Equal(before, await TodoCountAsync());
        Assert.Equal(1, await RowCountAsync(survivor));
    }

    [Fact]
    public async Task DeleteTodo_LeavesContactAndOtherTodosUnchanged_AC040()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var contactId = await TodoStoreTests.CreateContactAsync(client, "Keeper", $"keeper.{Guid.NewGuid():N}@example.com");
        var doomed = await CreateTodoAsync(client, "Doomed", contactId);
        var sibling = await CreateTodoAsync(client, "Sibling of same contact", contactId);
        var other = await CreateTodoAsync(client, "Unrelated");
        var contactBefore = await GetBodyAsync(client, $"/api/contacts/{contactId}");
        var siblingBefore = await GetBodyAsync(client, $"/api/todos/{sibling}");
        var otherBefore = await GetBodyAsync(client, $"/api/todos/{other}");
        var siblingRowBefore = await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, sibling);
        var otherRowBefore = await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, other);
        var total = await TodoCountAsync();
        factory.Time.Advance(TimeSpan.FromMinutes(10));

        var response = await client.DeleteAsync($"/api/todos/{doomed}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var reader = factory.CreateClient();
        Assert.Equal(contactBefore, await GetBodyAsync(reader, $"/api/contacts/{contactId}"));
        Assert.Equal(siblingBefore, await GetBodyAsync(reader, $"/api/todos/{sibling}"));
        Assert.Equal(otherBefore, await GetBodyAsync(reader, $"/api/todos/{other}"));
        Assert.Equal(siblingRowBefore, await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, sibling));
        Assert.Equal(otherRowBefore, await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, other));
        Assert.Equal(total - 1, await TodoCountAsync());
    }

    // The 404 branches of PUT/complete/reopen exist from T-07/T-08; at RED this fails only because the
    // delete in the arrange step gets 405.
    [Fact]
    public async Task UpdateCompleteReopen_AfterDelete_Return404AndDoNotRecreate_AC041()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var id = await CreateTodoAsync(client, "Deleted");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/todos/{id}", Ct)).StatusCode);
        var total = await TodoCountAsync();

        var put = await client.PutAsJsonAsync($"/api/todos/{id}", new { title = "Back" }, Ct);
        using var putProblem = await ProblemAssert.IsProblemAsync(put, 404);
        var complete = await client.PostAsync($"/api/todos/{id}/complete", null, Ct);
        using var completeProblem = await ProblemAssert.IsProblemAsync(complete, 404);
        var reopen = await client.PostAsync($"/api/todos/{id}/reopen", null, Ct);
        using var reopenProblem = await ProblemAssert.IsProblemAsync(reopen, 404);

        using var reader = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/api/todos/{id}", Ct)).StatusCode);
        Assert.Equal(0, await RowCountAsync(id));
        Assert.Equal(total, await TodoCountAsync());
    }

    [Fact]
    public async Task DeleteTodo_ConcurrentSameId_ExactlyOne204Rest404_AC071()
    {
        _ = factory.Server;
        const int requests = 10;
        using var seedClient = factory.CreateClient();
        var id = await CreateTodoAsync(seedClient, "Race");
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
                return await client.DeleteAsync($"/api/todos/{id}", Ct);
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
