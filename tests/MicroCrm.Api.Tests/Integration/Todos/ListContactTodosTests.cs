using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

namespace MicroCrm.Api.Tests.Integration.Todos;

// GET /api/contacts/{id}/todos (spec 003, T-12): same envelope, order, paging and status rules as the global list.
public sealed class ListContactTodosTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private DateOnly Today => DateOnly.FromDateTime(factory.Time.GetUtcNow().UtcDateTime);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd");

    private sealed record Seeded(Guid Id, DateTimeOffset CreatedAt, string? DueDate);

    private static async Task<Seeded> CreateAsync(
        HttpClient client, string title, string? due, Guid? contactId, bool done = false)
    {
        var response = await client.PostAsJsonAsync("/api/todos", new { title, dueDate = due, contactId }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var id = doc.RootElement.GetProperty("id").GetGuid();
        var createdAt = doc.RootElement.GetProperty("createdAt").GetDateTimeOffset();
        if (done)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/todos/{id}/complete", null, Ct)).StatusCode);
        }

        return new Seeded(id, createdAt, due);
    }

    private static Task<Guid> NewContactAsync(HttpClient client, string tag) =>
        TodoStoreTests.CreateContactAsync(client, "Nested", $"nested.{tag}.{Guid.NewGuid():N}@example.com");

    private static async Task<JsonDocument> ListOkAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
    }

    private static Guid[] Ids(JsonDocument doc) =>
        doc.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToArray();

    private static int Total(JsonDocument doc) => doc.RootElement.GetProperty("totalCount").GetInt32();

    private static string[] Messages(JsonElement errors, string key) =>
        errors.GetProperty(key).EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Fact]
    public async Task ListContactTodos_ExistingContact_ReturnsOnlyItsTodosOrdered_AC057()
    {
        using var client = factory.CreateClient();
        var contact = await NewContactAsync(client, "a");
        var other = await NewContactAsync(client, "b");
        var mine = new List<Seeded>();

        // Distinct createdAt, mixed and null due dates, a done to-do, interleaved with foreign and unlinked to-dos.
        mine.Add(await CreateAsync(client, "no date, first", null, contact));
        factory.Time.Advance(TimeSpan.FromSeconds(1));
        await CreateAsync(client, "other contact", "2026-03-01", other);
        mine.Add(await CreateAsync(client, "due Mar 2", "2026-03-02", contact));
        factory.Time.Advance(TimeSpan.FromSeconds(1));
        await CreateAsync(client, "unlinked", "2026-03-01", null);
        mine.Add(await CreateAsync(client, "due Mar 1 (done)", "2026-03-01", contact, done: true));
        factory.Time.Advance(TimeSpan.FromSeconds(1));
        mine.Add(await CreateAsync(client, "no date, second", null, contact));
        factory.Time.Advance(TimeSpan.FromSeconds(1));

        // Same due date, createdAt one tick apart inside one millisecond: ids are random, so only a real
        // createdAt sort key yields the creation order (as in ListTodos_SameDueDate_OrdersByCreatedAtNotId_AC044).
        var subMs = factory.Time.GetUtcNow().Ticks % TimeSpan.TicksPerMillisecond;
        if (subMs != 0)
        {
            factory.Time.Advance(TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond - subMs));
        }

        var sameDay = new List<Seeded>();
        for (var i = 0; i < 8; i++)
        {
            sameDay.Add(await CreateAsync(client, $"Same day {i}", "2026-08-01", contact));
            await CreateAsync(client, $"Foreign same day {i}", "2026-08-01", other);
            factory.Time.Advance(TimeSpan.FromTicks(1));
        }

        mine.AddRange(sameDay);
        Assert.Equal(8, sameDay.Select(s => s.CreatedAt).Distinct().Count());

        var expected = mine
            .OrderBy(s => s.DueDate is null ? 1 : 0)
            .ThenBy(s => s.DueDate, StringComparer.Ordinal)
            .ThenBy(s => s.CreatedAt)
            .ThenBy(s => s.Id.ToString().ToLowerInvariant(), StringComparer.Ordinal)
            .Select(s => s.Id)
            .ToArray();

        using var doc = await ListOkAsync(client, $"/api/contacts/{contact}/todos?pageSize=100");

        Assert.Equal(expected, Ids(doc));
        Assert.Equal(mine.Count, Total(doc));
        Assert.Equal(1, doc.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(100, doc.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(sameDay.Select(s => s.Id), Ids(doc).Where(id => sameDay.Any(s => s.Id == id)));
        // Items have the same shape as the global list.
        using var global = await ListOkAsync(client, $"/api/todos?contactId={contact}&pageSize=100");
        Assert.Equal(
            global.RootElement.GetProperty("items").GetRawText(),
            doc.RootElement.GetProperty("items").GetRawText());
    }

    private async Task<Guid> SeedForPagingAsync(HttpClient client)
    {
        var contact = await NewContactAsync(client, "paging");
        var other = await NewContactAsync(client, "paging-other");
        await CreateAsync(client, "overdue 1", Iso(Today.AddDays(-2)), contact);
        await CreateAsync(client, "overdue 2", Iso(Today.AddDays(-1)), contact);
        await CreateAsync(client, "future", Iso(Today.AddDays(1)), contact);
        await CreateAsync(client, "open no date", null, contact);
        await CreateAsync(client, "done past", Iso(Today.AddDays(-1)), contact, done: true);
        await CreateAsync(client, "done no date", null, contact, done: true);
        await CreateAsync(client, "foreign overdue", Iso(Today.AddDays(-3)), other);
        await CreateAsync(client, "foreign done", null, other, done: true);
        return contact;
    }

    [Theory]
    [InlineData("", 6, 6)]
    [InlineData("?pageSize=2", 6, 2)]
    [InlineData("?page=2&pageSize=4", 6, 2)]
    [InlineData("?page=3&pageSize=4", 6, 0)]
    [InlineData("?pageSize=100", 6, 6)]
    [InlineData("?status=open", 4, 4)]
    [InlineData("?status=done", 2, 2)]
    [InlineData("?status=overdue", 2, 2)]
    [InlineData("?status=", 6, 6)]
    [InlineData("?status=%20OVERDUE%20", 2, 2)]
    [InlineData("?status=open&page=2&pageSize=3", 4, 1)]
    [InlineData("?status=done&pageSize=1", 2, 1)]
    public async Task ListContactTodos_PagingAndStatus_AppliedLikeGlobalList_AC058(
        string query, int expectedTotal, int expectedItems)
    {
        using var client = factory.CreateClient();
        var contact = await SeedForPagingAsync(client);

        using var nested = await ListOkAsync(client, $"/api/contacts/{contact}/todos{query}");

        Assert.Equal(expectedTotal, Total(nested));
        Assert.Equal(expectedItems, Ids(nested).Length);
        // Same ids, in the same order, as the global list filtered to this contact.
        var globalQuery = query.Length == 0 ? $"?contactId={contact}" : $"{query}&contactId={contact}";
        using var global = await ListOkAsync(client, $"/api/todos{globalQuery}");
        Assert.Equal(Ids(global), Ids(nested));
        Assert.Equal(
            global.RootElement.GetProperty("page").GetInt32(),
            nested.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(
            global.RootElement.GetProperty("pageSize").GetInt32(),
            nested.RootElement.GetProperty("pageSize").GetInt32());
    }

    [Theory]
    [InlineData("?page=0", "page")]
    [InlineData("?page=abc", "page")]
    [InlineData("?pageSize=0", "pageSize")]
    [InlineData("?pageSize=101", "pageSize")]
    [InlineData("?pageSize=1.5", "pageSize")]
    [InlineData("?status=pending", "status")]
    [InlineData("?status=1", "status")]
    [InlineData("?status=open%2Cdone", "status")]
    [InlineData("?status=open%2C%20overdue", "status")]
    [InlineData("?status=done%2Cdone", "status")]
    [InlineData("?page=0&pageSize=101&status=pending", "page,pageSize,status")]
    public async Task ListContactTodos_InvalidQuery_Returns400SameAsGlobalList_AC058(string query, string keys)
    {
        using var client = factory.CreateClient();
        var contact = await SeedForPagingAsync(client);

        var nested = await client.GetAsync($"/api/contacts/{contact}/todos{query}", Ct);
        var global = await client.GetAsync($"/api/todos{query}", Ct);

        using var nestedProblem = await ProblemAssert.IsProblemAsync(nested, 400, keys.Split(','));
        using var globalProblem = await ProblemAssert.IsProblemAsync(global, 400, keys.Split(','));
        Assert.Equal(
            globalProblem.RootElement.GetProperty("errors").GetRawText(),
            nestedProblem.RootElement.GetProperty("errors").GetRawText());
    }

    [Fact]
    public async Task ListContactTodos_NoTodos_ReturnsEmptyItemsAndZeroTotal_AC059()
    {
        using var client = factory.CreateClient();
        var empty = await NewContactAsync(client, "empty");
        var busy = await NewContactAsync(client, "busy");
        await CreateAsync(client, "someone else's", null, busy);
        await CreateAsync(client, "unlinked", null, null);

        using var doc = await ListOkAsync(client, $"/api/contacts/{empty}/todos");

        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("items").ValueKind);
        Assert.Empty(Ids(doc));
        Assert.Equal(0, Total(doc));
        Assert.Equal(1, doc.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(20, doc.RootElement.GetProperty("pageSize").GetInt32());
    }

    [Theory]
    [InlineData("00000000-0000-7000-8000-000000000000", "")]
    [InlineData("not-a-guid", "")]
    [InlineData("123", "")]
    [InlineData("not-a-guid", "?page=0")]
    public async Task ListContactTodos_UnknownOrNonGuidContact_Returns404Problem_AC060(string id, string query)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/contacts/{id}/todos{query}", Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 404);
    }

    [Fact]
    public async Task ListContactTodos_UnknownRandomGuid_Returns404Problem_AC060()
    {
        using var client = factory.CreateClient();
        await CreateAsync(client, "unlinked", null, null);

        var response = await client.GetAsync($"/api/contacts/{Guid.CreateVersion7()}/todos", Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 404);
    }

    [Theory]
    [InlineData("?page=0", "page")]
    [InlineData("?pageSize=101", "pageSize")]
    [InlineData("?status=pending", "status")]
    [InlineData("?page=0&pageSize=101&status=pending", "page,pageSize,status")]
    public async Task ListContactTodos_InvalidQueryForUnknownContact_Returns400_AC061(string query, string keys)
    {
        using var client = factory.CreateClient();
        var unknown = Guid.CreateVersion7();

        var invalid = await client.GetAsync($"/api/contacts/{unknown}/todos{query}", Ct);
        var valid = await client.GetAsync($"/api/contacts/{unknown}/todos", Ct);

        using var problem = await ProblemAssert.IsProblemAsync(invalid, 400, keys.Split(','));
        // The same contact id is a 404 once the query is valid, so the 400 really came before the lookup.
        using var notFound = await ProblemAssert.IsProblemAsync(valid, 404);
    }

    // Guard: the contact comes from the route; a contactId query value is ignored, valid or not.
    [Theory]
    [InlineData("?contactId=not-a-guid")]
    [InlineData("?contactId=OTHER")]
    [InlineData("?contactId=OTHER&status=open&pageSize=100")]
    public async Task ListContactTodos_ContactIdQueryValue_IsIgnored_AC057(string query)
    {
        using var client = factory.CreateClient();
        var contact = await NewContactAsync(client, "route");
        var other = await NewContactAsync(client, "route-other");
        var mine = await CreateAsync(client, "mine", null, contact);
        await CreateAsync(client, "theirs", null, other);
        await CreateAsync(client, "unlinked", null, null);

        using var doc = await ListOkAsync(client, $"/api/contacts/{contact}/todos{query.Replace("OTHER", other.ToString())}");

        Assert.Equal([mine.Id], Ids(doc));
        Assert.Equal(1, Total(doc));
    }

    [Fact]
    public async Task ListContactTodos_DeletedContact_Returns404_AC068()
    {
        using var client = factory.CreateClient();
        var contact = await NewContactAsync(client, "gone");
        var todo = await CreateAsync(client, "former", null, contact);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/contacts/{contact}", Ct)).StatusCode);
        // Before the delete the nested list works, so the 404 below is caused by the delete.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/contacts/{contact}/todos", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/contacts/{contact}", Ct)).StatusCode);
        var response = await client.GetAsync($"/api/contacts/{contact}/todos", Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 404);
        // The former to-do still exists, unlinked.
        using var all = await ListOkAsync(client, "/api/todos");
        Assert.Equal([todo.Id], Ids(all));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ListContactTodos_ValidationMessages_FollowStyle_AC074(bool contactExists)
    {
        using var client = factory.CreateClient();
        var contact = contactExists ? await NewContactAsync(client, "style") : Guid.CreateVersion7();

        var response = await client.GetAsync($"/api/contacts/{contact}/todos?page=0&pageSize=101&status=pending", Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "page", "pageSize", "status");
        ProblemAssert.AssertValidationMessageStyle(problem);
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["Must be an integer between 1 and 2147483647."], Messages(errors, "page"));
        Assert.Equal(["Must be an integer between 1 and 100."], Messages(errors, "pageSize"));
        Assert.Equal(["Must be one of: open, done, overdue."], Messages(errors, "status"));
    }
}
