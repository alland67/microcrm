using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

namespace MicroCrm.Api.Tests.Integration.Todos;

// Complete and reopen a to-do (spec 003, T-08). Each test uses its own data, so the shared class fixture needs no reset.
public sealed class CompleteReopenTodoTests(ApiFactory factory) : IClassFixture<ApiFactory>
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

    private static async Task<HttpResponseMessage> ActAsync(HttpClient client, Guid id, string action, string? rawBody = null) =>
        await client.PostAsync(
            $"/api/todos/{id}/{action}",
            rawBody is null ? null : new StringContent(rawBody, Encoding.UTF8, "application/json"),
            Ct);

    private static async Task<JsonDocument> OkBodyAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(body);
    }

    private static async Task<string> GetBodyAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/todos/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private async Task<long> TodoCountAsync() =>
        await TodoStoreTests.ScalarAsync(factory.ConnectionString, "SELECT COUNT(*) FROM Todos");

    [Fact]
    public async Task CompleteTodo_Open_Returns200DoneWithCompletedAtNow_AC031()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var id = await CreateTodoAsync(client, "Finish me");
        factory.Time.Advance(TimeSpan.FromHours(1));

        var response = await ActAsync(client, id, "complete");

        using var doc = await OkBodyAsync(response);
        var now = factory.Time.GetUtcNow();
        Assert.Equal(id, doc.RootElement.GetProperty("id").GetGuid());
        Assert.True(doc.RootElement.GetProperty("isDone").GetBoolean());
        Assert.Equal(now, doc.RootElement.GetProperty("completedAt").GetDateTimeOffset());
        Assert.Equal(now, doc.RootElement.GetProperty("updatedAt").GetDateTimeOffset());

        using var fresh = factory.CreateClient();
        using var fetched = JsonDocument.Parse(await GetBodyAsync(fresh, id));
        Assert.Equal(doc.RootElement.GetRawText(), fetched.RootElement.GetRawText());
    }

    [Fact]
    public async Task CompleteTodo_AlreadyDone_Returns200Unchanged_AC032()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var id = await CreateTodoAsync(client, "Done already");
        factory.Time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(HttpStatusCode.OK, (await ActAsync(client, id, "complete")).StatusCode);
        var doneBody = await GetBodyAsync(client, id);
        var rowBefore = await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, id);
        factory.Time.Advance(TimeSpan.FromHours(2));

        var response = await ActAsync(client, id, "complete");

        using var doc = await OkBodyAsync(response);
        using var before = JsonDocument.Parse(doneBody);
        Assert.Equal(before.RootElement.GetRawText(), doc.RootElement.GetRawText());
        Assert.True(doc.RootElement.GetProperty("isDone").GetBoolean());
        Assert.Equal(rowBefore, await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, id));
        Assert.Equal(doneBody, await GetBodyAsync(client, id));
    }

    [Fact]
    public async Task ReopenTodo_Done_Returns200OpenWithUpdatedAtNow_AC033()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var id = await CreateTodoAsync(client, "Reopen me");
        factory.Time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(HttpStatusCode.OK, (await ActAsync(client, id, "complete")).StatusCode);
        factory.Time.Advance(TimeSpan.FromHours(1));

        var response = await ActAsync(client, id, "reopen");

        using var doc = await OkBodyAsync(response);
        var now = factory.Time.GetUtcNow();
        Assert.False(doc.RootElement.GetProperty("isDone").GetBoolean());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("completedAt").ValueKind);
        Assert.Equal(now, doc.RootElement.GetProperty("updatedAt").GetDateTimeOffset());

        using var fresh = factory.CreateClient();
        using var fetched = JsonDocument.Parse(await GetBodyAsync(fresh, id));
        Assert.Equal(doc.RootElement.GetRawText(), fetched.RootElement.GetRawText());
    }

    [Fact]
    public async Task ReopenTodo_AlreadyOpen_Returns200Unchanged_AC034()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var id = await CreateTodoAsync(client, "Still open");
        var openBody = await GetBodyAsync(client, id);
        var rowBefore = await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, id);
        factory.Time.Advance(TimeSpan.FromHours(2));

        var response = await ActAsync(client, id, "reopen");

        using var doc = await OkBodyAsync(response);
        using var before = JsonDocument.Parse(openBody);
        Assert.Equal(before.RootElement.GetRawText(), doc.RootElement.GetRawText());
        Assert.False(doc.RootElement.GetProperty("isDone").GetBoolean());
        Assert.Equal(rowBefore, await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, id));
    }

    [Theory]
    [InlineData("complete", """{"title":"x","isDone":false,"completedAt":null}""")]
    [InlineData("complete", "{")]
    [InlineData("reopen", """{"title":"x","isDone":true,"completedAt":"1999-01-01T00:00:00+00:00"}""")]
    [InlineData("reopen", "{")]
    public async Task CompleteAndReopen_ChangeOnlyDoneStateAndIgnoreBody_AC035(string action, string rawBody)
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var contactId = await TodoStoreTests.CreateContactAsync(client, "Act", $"act.{Guid.NewGuid():N}@example.com");
        var id = await CreateTodoAsync(client, "Target", contactId);
        var other = await CreateTodoAsync(client, "Bystander");
        if (action == "reopen")
        {
            factory.Time.Advance(TimeSpan.FromMinutes(30));
            Assert.Equal(HttpStatusCode.OK, (await ActAsync(client, id, "complete")).StatusCode);
        }

        using var targetBefore = JsonDocument.Parse(await GetBodyAsync(client, id));
        var otherBefore = await GetBodyAsync(client, other);
        var otherRowBefore = await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, other);
        factory.Time.Advance(TimeSpan.FromHours(1));

        var response = await ActAsync(client, id, action, rawBody);

        using var doc = await OkBodyAsync(response);
        var after = doc.RootElement;
        var before = targetBefore.RootElement;
        foreach (var name in new[] { "id", "title", "notes", "dueDate", "contactId", "createdAt" })
        {
            Assert.Equal(before.GetProperty(name).GetRawText(), after.GetProperty(name).GetRawText());
        }

        Assert.Equal(action == "complete", after.GetProperty("isDone").GetBoolean());
        Assert.Equal(factory.Time.GetUtcNow(), after.GetProperty("updatedAt").GetDateTimeOffset());
        if (action == "complete")
        {
            Assert.Equal(factory.Time.GetUtcNow(), after.GetProperty("completedAt").GetDateTimeOffset());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, after.GetProperty("completedAt").ValueKind);
        }

        Assert.Equal(otherBefore, await GetBodyAsync(client, other));
        Assert.Equal(otherRowBefore, await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, other));
    }

    public static TheoryData<string, string> UnknownIds()
    {
        var data = new TheoryData<string, string>();
        foreach (var action in new[] { "complete", "reopen" })
        {
            data.Add(action, Guid.CreateVersion7().ToString());
            data.Add(action, "not-a-guid");
        }

        return data;
    }

    // Guard at RED: the 404 problem+json already comes from the complete/reopen routes (unknown GUID) or from
    // status code pages (non-GUID id, no route match).
    [Theory]
    [MemberData(nameof(UnknownIds))]
    public async Task CompleteOrReopen_UnknownOrNonGuidId_Returns404_AC036(string action, string id)
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var before = await TodoCountAsync();

        var response = await client.PostAsync($"/api/todos/{id}/{action}", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var problem = await ProblemAssert.IsProblemAsync(response, 404);

        Assert.Equal(before, await TodoCountAsync());
    }

    [Fact]
    public async Task UpdateTodo_WhenDone_KeepsDoneAndCompletedAt_AC037()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var id = await CreateTodoAsync(client, "Done then edited");
        factory.Time.Advance(TimeSpan.FromHours(1));
        using var completed = await OkBodyAsync(await ActAsync(client, id, "complete"));
        var completedAt = completed.RootElement.GetProperty("completedAt").GetDateTimeOffset();
        factory.Time.Advance(TimeSpan.FromHours(1));

        var put = await client.PutAsync(
            $"/api/todos/{id}",
            new StringContent("""{"title":"Edited","isDone":false,"completedAt":null}""", Encoding.UTF8, "application/json"),
            Ct);

        using var doc = await OkBodyAsync(put);
        using var fresh = factory.CreateClient();
        using var fetched = JsonDocument.Parse(await GetBodyAsync(fresh, id));
        foreach (var root in new[] { doc.RootElement, fetched.RootElement })
        {
            Assert.Equal("Edited", root.GetProperty("title").GetString());
            Assert.True(root.GetProperty("isDone").GetBoolean());
            Assert.Equal(completedAt, root.GetProperty("completedAt").GetDateTimeOffset());
        }
    }
}
