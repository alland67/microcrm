using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

namespace MicroCrm.Api.Tests.Integration.Todos;

// List to-dos: status and contactId filters (spec 003, T-11). Every date is derived from the fake clock.
public sealed class ListTodosFilterTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private DateOnly Today => DateOnly.FromDateTime(factory.Time.GetUtcNow().UtcDateTime);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd");

    private sealed record Set(Guid OpenOverdue, Guid OpenToday, Guid OpenTomorrow, Guid OpenNoDate, Guid DoneOverdue, Guid DoneNoDate);

    private static async Task<Guid> CreateAsync(
        HttpClient client, string title, DateOnly? due, Guid? contactId = null, bool done = false)
    {
        var response = await client.PostAsJsonAsync(
            "/api/todos", new { title, dueDate = due is null ? null : Iso(due.Value), contactId }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var id = doc.RootElement.GetProperty("id").GetGuid();
        if (done)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/todos/{id}/complete", null, Ct)).StatusCode);
        }

        return id;
    }

    private async Task<Set> SeedMixedAsync(HttpClient client) => new(
        OpenOverdue: await CreateAsync(client, "open yesterday", Today.AddDays(-1)),
        OpenToday: await CreateAsync(client, "open today", Today),
        OpenTomorrow: await CreateAsync(client, "open tomorrow", Today.AddDays(1)),
        OpenNoDate: await CreateAsync(client, "open no date", null),
        DoneOverdue: await CreateAsync(client, "done yesterday", Today.AddDays(-1), done: true),
        DoneNoDate: await CreateAsync(client, "done no date", null, done: true));

    private static async Task<JsonDocument> ListAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync("/api/todos" + query, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
    }

    private static Guid[] Ids(JsonDocument doc) =>
        doc.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToArray();

    private static int Total(JsonDocument doc) => doc.RootElement.GetProperty("totalCount").GetInt32();

    private static string[] Messages(JsonElement errors, string key) =>
        errors.GetProperty(key).EnumerateArray().Select(e => e.GetString()!).ToArray();

    private static Task<Guid> NewContactAsync(HttpClient client, string tag) =>
        TodoStoreTests.CreateContactAsync(client, "Filter", $"filter.{tag}.{Guid.NewGuid():N}@example.com");

    [Fact]
    public async Task ListTodos_StatusOpen_ReturnsOnlyOpenIncludingOverdue_AC047()
    {
        using var client = factory.CreateClient();
        var s = await SeedMixedAsync(client);

        using var doc = await ListAsync(client, "?status=open");

        Assert.Equal(
            new[] { s.OpenOverdue, s.OpenToday, s.OpenTomorrow, s.OpenNoDate }.Order().ToArray(),
            Ids(doc).Order().ToArray());
        Assert.Equal(4, Total(doc));
    }

    [Fact]
    public async Task ListTodos_StatusDone_ReturnsOnlyDone_AC048()
    {
        using var client = factory.CreateClient();
        var s = await SeedMixedAsync(client);

        using var doc = await ListAsync(client, "?status=done");

        Assert.Equal(new[] { s.DoneOverdue, s.DoneNoDate }.Order().ToArray(), Ids(doc).Order().ToArray());
        Assert.Equal(2, Total(doc));
    }

    [Fact]
    public async Task ListTodos_StatusOverdue_ReturnsOnlyOpenPastDue_AC049()
    {
        using var client = factory.CreateClient();
        var s = await SeedMixedAsync(client);

        using var doc = await ListAsync(client, "?status=overdue");

        Assert.Equal([s.OpenOverdue], Ids(doc));
        Assert.Equal(1, Total(doc));
    }

    [Theory]
    [InlineData("", 6)]
    [InlineData("  ", 6)]
    [InlineData("OPEN", 4)]
    [InlineData(" Done ", 2)]
    [InlineData("OverDue", 1)]
    public async Task ListTodos_StatusBlankOrDifferentCase_AC051(string status, int expectedTotal)
    {
        using var client = factory.CreateClient();
        await SeedMixedAsync(client);

        using var doc = await ListAsync(client, $"?status={Uri.EscapeDataString(status)}");

        Assert.Equal(expectedTotal, Total(doc));
        Assert.Equal(expectedTotal, Ids(doc).Length);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("opened")]
    [InlineData("1")]
    [InlineData("open,done")]
    [InlineData("open, overdue")]
    [InlineData("done,done")]
    public async Task ListTodos_StatusUnknown_Returns400_AC052(string status)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/todos?status={Uri.EscapeDataString(status)}", Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "status");
        Assert.Equal(["Must be one of: open, done, overdue."], Messages(problem.RootElement.GetProperty("errors"), "status"));
    }

    [Fact]
    public async Task ListTodos_ContactIdFilter_ReturnsOnlyThatContactsTodos_AC053()
    {
        using var client = factory.CreateClient();
        var a = await NewContactAsync(client, "a");
        var b = await NewContactAsync(client, "b");
        var a1 = await CreateAsync(client, "a1", Today, a);
        var a2 = await CreateAsync(client, "a2", null, a);
        var b1 = await CreateAsync(client, "b1", Today, b);
        var loose = await CreateAsync(client, "loose", Today);

        using var forA = await ListAsync(client, $"?contactId={a}");
        using var forB = await ListAsync(client, $"?contactId={b}");
        using var unknown = await ListAsync(client, $"?contactId={Guid.CreateVersion7()}");
        using var blank = await ListAsync(client, "?contactId=");
        using var whitespace = await ListAsync(client, "?contactId=%20%20");
        using var padded = await ListAsync(client, $"?contactId=%20{a}%20");

        Assert.Equal([a1, a2], Ids(forA));
        Assert.Equal(2, Total(forA));
        Assert.Equal([b1], Ids(forB));
        Assert.Empty(Ids(unknown));
        Assert.Equal(0, Total(unknown));
        Assert.Equal(4, Total(blank));
        Assert.Equal(4, Total(whitespace));
        Assert.Equal([a1, a2], Ids(padded));
        Assert.Contains(loose, Ids(blank));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("123")]
    [InlineData("not-a-guid")]
    public async Task ListTodos_MalformedContactId_Returns400_AC054(string contactId)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/todos?contactId={contactId}", Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "contactId");
        Assert.Equal(["Must be a valid GUID."], Messages(problem.RootElement.GetProperty("errors"), "contactId"));
    }

    [Fact]
    public async Task ListTodos_CombinedFilters_MatchAllOrderedAndPaged_AC055()
    {
        using var client = factory.CreateClient();
        var c = await NewContactAsync(client, "c");
        var other = await NewContactAsync(client, "other");
        // Matching: open, past due, linked to c. Created out of due-date order to prove ordering.
        var d3 = await CreateAsync(client, "d3", Today.AddDays(-3), c);
        var d1 = await CreateAsync(client, "d1", Today.AddDays(-1), c);
        var d2 = await CreateAsync(client, "d2", Today.AddDays(-2), c);
        // Non-matching.
        await CreateAsync(client, "done", Today.AddDays(-5), c, done: true);
        await CreateAsync(client, "today", Today, c);
        await CreateAsync(client, "undated", null, c);
        await CreateAsync(client, "other contact", Today.AddDays(-4), other);
        await CreateAsync(client, "unlinked", Today.AddDays(-4));

        using var first = await ListAsync(client, $"?status=overdue&contactId={c}&page=1&pageSize=2");
        using var second = await ListAsync(client, $"?status=overdue&contactId={c}&page=2&pageSize=2");

        Assert.Equal([d3, d2], Ids(first));
        Assert.Equal([d1], Ids(second));
        Assert.Equal(3, Total(first));
        Assert.Equal(3, Total(second));
        Assert.Equal(1, first.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(2, second.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(2, second.RootElement.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task ListTodos_AllQueryParametersInvalid_ReportsAll_AC056()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/todos?page=0&pageSize=101&status=pending&contactId=nope", Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "contactId", "page", "pageSize", "status");
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["Must be an integer between 1 and 2147483647."], Messages(errors, "page"));
        Assert.Equal(["Must be an integer between 1 and 100."], Messages(errors, "pageSize"));
        Assert.Equal(["Must be one of: open, done, overdue."], Messages(errors, "status"));
        Assert.Equal(["Must be a valid GUID."], Messages(errors, "contactId"));
    }

    [Fact]
    public async Task ListTodos_ContactIdOfDeletedContact_ExcludesFormerTodos_AC068()
    {
        using var client = factory.CreateClient();
        var contact = await NewContactAsync(client, "gone");
        var todo = await CreateAsync(client, "former", Today, contact);
        using (var before = await ListAsync(client, $"?contactId={contact}"))
        {
            Assert.Equal([todo], Ids(before));
        }

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/contacts/{contact}", Ct)).StatusCode);

        using var after = await ListAsync(client, $"?contactId={contact}");
        Assert.Empty(Ids(after));
        Assert.Equal(0, Total(after));
        using var all = await ListAsync(client, "");
        Assert.Equal([todo], Ids(all));
    }

    [Fact]
    public async Task ListTodos_FilterMessages_FollowStyle_AC074()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/todos?status=pending&contactId=nope", Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "contactId", "status");
        ProblemAssert.AssertValidationMessageStyle(problem);
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["Must be one of: open, done, overdue."], Messages(errors, "status"));
        Assert.Equal(["Must be a valid GUID."], Messages(errors, "contactId"));
    }
}
