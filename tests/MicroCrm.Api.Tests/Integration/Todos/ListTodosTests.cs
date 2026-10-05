using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

namespace MicroCrm.Api.Tests.Integration.Todos;

// List to-dos: envelope and order (spec 003, T-10). Each test starts from an empty store.
public sealed class ListTodosTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed record Seeded(Guid Id, string? DueDate, DateTimeOffset CreatedAt);

    private static async Task<Seeded> CreateTodoAsync(
        HttpClient client, string title, string? dueDate = null, Guid? contactId = null)
    {
        var response = await client.PostAsJsonAsync("/api/todos", new { title, dueDate, contactId }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return new Seeded(
            doc.RootElement.GetProperty("id").GetGuid(),
            doc.RootElement.GetProperty("dueDate").GetString(),
            doc.RootElement.GetProperty("createdAt").GetDateTimeOffset());
    }

    private static async Task<JsonDocument> ListAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync("/api/todos" + query, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
    }

    private static Guid[] Ids(JsonDocument doc) =>
        doc.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToArray();

    [Fact]
    public async Task ListTodos_NoParameters_ReturnsDefaultEnvelope_AC042()
    {
        using var client = factory.CreateClient();
        for (var i = 0; i < 25; i++)
        {
            await CreateTodoAsync(client, $"Todo {i}");
        }

        using var doc = await ListAsync(client);

        var root = doc.RootElement;
        Assert.Equal(20, root.GetProperty("items").GetArrayLength());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(20, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(25, root.GetProperty("totalCount").GetInt32());
        var members = root.GetProperty("items")[0].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(
            new[] { "completedAt", "contactId", "createdAt", "dueDate", "id", "isDone", "notes", "title", "updatedAt" },
            members);
    }

    [Fact]
    public async Task ListTodos_Empty_ReturnsEmptyItemsAndZeroTotal_AC043()
    {
        using var client = factory.CreateClient();

        using var doc = await ListAsync(client);

        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("items").ValueKind);
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(20, doc.RootElement.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task ListTodos_OrdersByDueDateNullsLastThenCreatedAtThenId_AC044()
    {
        using var client = factory.CreateClient();
        var seeded = new List<Seeded>();

        // Distinct createdAt: later-created items have earlier or equal due dates and vice versa.
        seeded.Add(await CreateTodoAsync(client, "no date, first", null));
        factory.Time.Advance(TimeSpan.FromSeconds(1));
        seeded.Add(await CreateTodoAsync(client, "due Mar 2, first", "2026-03-02"));
        factory.Time.Advance(TimeSpan.FromSeconds(1));
        seeded.Add(await CreateTodoAsync(client, "due Mar 1", "2026-03-01"));
        factory.Time.Advance(TimeSpan.FromSeconds(1));
        seeded.Add(await CreateTodoAsync(client, "due Mar 2, second", "2026-03-02"));
        factory.Time.Advance(TimeSpan.FromSeconds(1));
        seeded.Add(await CreateTodoAsync(client, "no date, second", null));
        factory.Time.Advance(TimeSpan.FromSeconds(1));

        // Equal due date and equal createdAt (no clock advance): ordered by id only.
        for (var i = 0; i < 6; i++)
        {
            seeded.Add(await CreateTodoAsync(client, $"tie dated {i}", "2026-03-05"));
        }

        factory.Time.Advance(TimeSpan.FromSeconds(1));
        for (var i = 0; i < 6; i++)
        {
            seeded.Add(await CreateTodoAsync(client, $"tie undated {i}", null));
        }

        // Guard the fixture: the ties really are ties.
        Assert.Single(seeded.Where(s => s.DueDate == "2026-03-05").Select(s => s.CreatedAt).Distinct());
        Assert.Equal(6, seeded.Count(s => s.DueDate == "2026-03-05"));

        // Lowercase ordinal string order, not Guid.CompareTo.
        var expected = seeded
            .OrderBy(s => s.DueDate is null ? 1 : 0)
            .ThenBy(s => s.DueDate, StringComparer.Ordinal)
            .ThenBy(s => s.CreatedAt)
            .ThenBy(s => s.Id.ToString().ToLowerInvariant(), StringComparer.Ordinal)
            .Select(s => s.Id)
            .ToArray();

        using var doc = await ListAsync(client, "?pageSize=100");

        Assert.Equal(expected, Ids(doc));
        Assert.Equal(seeded.Count, doc.RootElement.GetProperty("totalCount").GetInt32());
    }

    // Ids are Guid.CreateVersion7(now): the millisecond prefix comes from the fake clock, so to-dos created
    // within the same millisecond have random ids. One-tick steps give distinct createdAt values whose order
    // is independent of id order, so only a real createdAt sort key yields the expected order.
    [Fact]
    public async Task ListTodos_SameDueDate_OrdersByCreatedAtNotId_AC044()
    {
        using var client = factory.CreateClient();
        // Align to a millisecond boundary so all creates share one millisecond.
        var subMs = factory.Time.GetUtcNow().Ticks % TimeSpan.TicksPerMillisecond;
        if (subMs != 0)
        {
            factory.Time.Advance(TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond - subMs));
        }

        var seeded = new List<Seeded>();
        for (var i = 0; i < 8; i++)
        {
            seeded.Add(await CreateTodoAsync(client, $"Same day {i}", "2026-08-01"));
            factory.Time.Advance(TimeSpan.FromTicks(1));
        }

        Assert.Equal(8, seeded.Select(s => s.CreatedAt).Distinct().Count());

        using var doc = await ListAsync(client);

        Assert.Equal(seeded.Select(s => s.Id), Ids(doc));
    }

    [Fact]
    public async Task ListTodos_ShowsUpdatedValues_AC020()
    {
        using var client = factory.CreateClient();
        var changed = await CreateTodoAsync(client, "Before", "2026-04-01");
        var other = await CreateTodoAsync(client, "Other", "2026-04-02");
        factory.Time.Advance(TimeSpan.FromMinutes(5));
        var put = await client.PutAsJsonAsync(
            $"/api/todos/{changed.Id}", new { title = "After", notes = "New notes", dueDate = "2026-04-03" }, Ct);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        using var reader = factory.CreateClient();
        var get = await reader.GetAsync($"/api/todos/{changed.Id}", Ct);
        using var expected = JsonDocument.Parse(await get.Content.ReadAsStringAsync(Ct));
        using var doc = await ListAsync(reader);

        Assert.Equal([other.Id, changed.Id], Ids(doc));
        var item = doc.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == changed.Id);
        Assert.Equal(expected.RootElement.GetRawText(), item.GetRawText());
        Assert.Equal("After", item.GetProperty("title").GetString());
        Assert.Equal("New notes", item.GetProperty("notes").GetString());
        Assert.Equal("2026-04-03", item.GetProperty("dueDate").GetString());
    }

    [Fact]
    public async Task ListTodos_ExcludesDeletedTodo_AC038()
    {
        using var client = factory.CreateClient();
        var keep = await CreateTodoAsync(client, "Keep");
        var doomed = await CreateTodoAsync(client, "Doomed");
        var before = await ListAsync(client);
        Assert.Contains(doomed.Id, Ids(before));
        before.Dispose();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/todos/{doomed.Id}", Ct)).StatusCode);

        using var reader = factory.CreateClient();
        using var doc = await ListAsync(reader);
        Assert.Equal([keep.Id], Ids(doc));
        Assert.Equal(1, doc.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task ListTodos_AfterContactDelete_ShowsTodosUnlinkedAndOtherwiseUnchanged_AC063()
    {
        using var client = factory.CreateClient();
        var contactId = await TodoStoreTests.CreateContactAsync(client, "Leaving", $"leaving.{Guid.NewGuid():N}@example.com");
        var open = await CreateTodoAsync(client, "Open linked", "2026-05-01", contactId);
        var done = await CreateTodoAsync(client, "Done linked", "2026-05-02", contactId);
        var loose = await CreateTodoAsync(client, "Unlinked", "2026-05-03");
        factory.Time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/todos/{done.Id}/complete", null, Ct)).StatusCode);

        using (var linked = await ListAsync(client))
        {
            var linkedItems = linked.RootElement.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(2, linkedItems.Count(i => i.GetProperty("contactId").GetString() == contactId.ToString()));
        }

        static Dictionary<Guid, Dictionary<string, JsonElement>> Snapshot(JsonDocument d) =>
            d.RootElement.GetProperty("items").EnumerateArray().ToDictionary(
                i => i.GetProperty("id").GetGuid(),
                i => i.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()));

        Dictionary<Guid, Dictionary<string, JsonElement>> before;
        using (var doc = await ListAsync(client))
        {
            before = Snapshot(doc);
        }

        factory.Time.Advance(TimeSpan.FromHours(2));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/contacts/{contactId}", Ct)).StatusCode);

        using var reader = factory.CreateClient();
        using var after = await ListAsync(reader);
        var afterItems = Snapshot(after);
        Assert.Equal(3, afterItems.Count);
        Assert.Equal(3, after.RootElement.GetProperty("totalCount").GetInt32());
        foreach (var id in new[] { open.Id, done.Id, loose.Id })
        {
            var b = before[id];
            var a = afterItems[id];
            Assert.Equal(b.Keys.Order().ToArray(), a.Keys.Order().ToArray());
            Assert.Equal(JsonValueKind.Null, a["contactId"].ValueKind);
            foreach (var key in b.Keys.Where(k => k != "contactId"))
            {
                Assert.Equal(b[key].GetRawText(), a[key].GetRawText());
            }
        }

        Assert.True(afterItems[done.Id]["isDone"].GetBoolean());
        Assert.Equal(JsonValueKind.String, afterItems[done.Id]["completedAt"].ValueKind);
        Assert.Equal(contactId.ToString(), before[open.Id]["contactId"].GetString());
        Assert.Equal(contactId.ToString(), before[done.Id]["contactId"].GetString());
    }
}
