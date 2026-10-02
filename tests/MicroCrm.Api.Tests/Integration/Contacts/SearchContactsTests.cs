using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

namespace MicroCrm.Api.Tests.Integration.Contacts;

public sealed class SearchContactsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Creates contacts in the given order, advancing the clock between creates (as the paging tests do).
    private async Task SeedAsync(HttpClient client, params (string First, string? Last, string? Email)[] seeds)
    {
        foreach (var (first, last, email) in seeds)
        {
            factory.Time.Advance(TimeSpan.FromMilliseconds(5));
            var response = await client.PostAsJsonAsync(
                "/api/contacts",
                new { firstName = first, lastName = last, email },
                Ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
    }

    private static async Task<string> GetBodyAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync("/api/contacts" + query, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private static async Task<(string[] Names, int TotalCount)> ListAsync(HttpClient client, string query)
    {
        using var doc = JsonDocument.Parse(await GetBodyAsync(client, query));
        var names = doc.RootElement.GetProperty("items").EnumerateArray()
            .Select(i => $"{i.GetProperty("firstName").GetString()} {i.GetProperty("lastName").GetString() ?? "(none)"}")
            .ToArray();
        return (names, doc.RootElement.GetProperty("totalCount").GetInt32());
    }

    private async Task SeedFamousAsync(HttpClient client) =>
        await SeedAsync(
            client,
            ("Ada", "Lovelace", "ada@analytical.test"),
            ("Grace", "Hopper", "gh1@navy.test"),
            ("Alan", "Turing", "alan.turing@computing.test"),
            ("Linus", "Torvalds", "linus@hop.test"),
            ("Margaret", "Hamilton", null), // no email (AC-006)
            ("Katherine", null, "kj@nasa.test"), // no last name (AC-006)
            ("Mary", null, null)); // neither optional field (AC-006)

    [Theory]
    [InlineData("LOVE", "Ada Lovelace")] // last name, upper-case term
    [InlineData("gRaCe", "Grace Hopper")] // first name, mixed-case term
    [InlineData("COMPUTING", "Alan Turing")] // email only, upper-case term
    [InlineData("Torv", "Linus Torvalds")] // last name, prefix substring
    [InlineData("HAMIL", "Margaret Hamilton")] // last name hit while email is null
    [InlineData("NASA", "Katherine (none)")] // email hit while last name is null
    [InlineData("mary", "Mary (none)")] // first name hit while last name and email are null
    public async Task ListContacts_Search_MatchesAnyFieldIgnoringCase_AC029(string term, string expected)
    {
        using var client = factory.CreateClient();
        await SeedFamousAsync(client);

        var (names, total) = await ListAsync(client, $"?search={term}");

        Assert.Equal(new[] { expected }, names);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task ListContacts_SearchHitsInDifferentFields_ReturnsAllWithMatchingTotal_AC029()
    {
        using var client = factory.CreateClient();
        await SeedFamousAsync(client);

        // "HOP" is in Hopper's last name and in Linus's email only.
        var (names, total) = await ListAsync(client, "?search=HOP");

        Assert.Equal(new[] { "Grace Hopper", "Linus Torvalds" }, names);
        Assert.Equal(2, total);
    }

    [Fact]
    public async Task ListContacts_SearchAcrossFirstAndLast_DoesNotMatch_AC030()
    {
        using var client = factory.CreateClient();
        await SeedFamousAsync(client);

        // Preconditions: each half matches on its own, so the empty result below is not vacuous.
        Assert.Equal(1, (await ListAsync(client, "?search=Ada")).TotalCount);
        Assert.Equal(1, (await ListAsync(client, "?search=Love")).TotalCount);

        var (names, total) = await ListAsync(client, "?search=Ada%20Love");

        Assert.Empty(names);
        Assert.Equal(0, total);
    }

    // GUARD: passes at RED because search is ignored today. It catches a regression where a blank
    // search is treated as a term, e.g. trimming missed so it becomes LIKE '%  %' (matches almost
    // nothing), or an empty term becoming LIKE '%%' with a different count/ordering path.
    [Theory]
    [InlineData("?search=")]
    [InlineData("?search=%20%20")]
    [InlineData("?search=%09%20")]
    public async Task ListContacts_BlankSearch_SameAsNoSearch_AC031(string query)
    {
        using var client = factory.CreateClient();
        await SeedFamousAsync(client);

        var baseline = await GetBodyAsync(client, "");
        var actual = await GetBodyAsync(client, query);

        Assert.Equal(baseline, actual);
        using var doc = JsonDocument.Parse(actual);
        Assert.Equal(7, doc.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task ListContacts_SearchWithSurroundingWhitespace_UsesTrimmedTerm_AC032()
    {
        using var client = factory.CreateClient();
        await SeedFamousAsync(client);

        var (names, total) = await ListAsync(client, "?search=%20lace%20");

        Assert.Equal(new[] { "Ada Lovelace" }, names);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task ListContacts_SearchNoMatches_ReturnsEmptyAndZero_AC035()
    {
        using var client = factory.CreateClient();
        await SeedFamousAsync(client);

        var body = await GetBodyAsync(client, "?search=zzzz");

        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Equal(0, items.GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task ListContacts_SearchWithPaging_FiltersThenSortsThenPages_AC036()
    {
        using var client = factory.CreateClient();

        // Seeded unsorted, non-matches mixed in. Matches for "mith" sorted by last name then first name:
        // Blacksmith, Goldsmith, Smith (Amy), Smith (Zed), Smithers, Smithson.
        await SeedAsync(
            client,
            ("Zed", "Smith", "s1@x.test"),
            ("Pat", "Adams", "s2@x.test"),
            ("Ann", "Smithson", "s3@x.test"),
            ("Quin", "Young", "s4@x.test"),
            ("Bob", "Blacksmith", "s5@x.test"),
            ("Rae", "Nelson", "s6@x.test"),
            ("Amy", "Smith", "s7@x.test"),
            ("Sue", "Zimmer", "s8@x.test"),
            ("Cal", "Smithers", "s9@x.test"),
            ("Tom", "Goldsmith", "s10@x.test"));

        var (names, total) = await ListAsync(client, "?search=mith&page=2&pageSize=2");

        Assert.Equal(new[] { "Amy Smith", "Zed Smith" }, names);
        Assert.Equal(6, total);
    }

    // The "%" and "_" rows are RED today (they act as wildcards and also return the decoys).
    // GUARD: the "\" row passes at RED because SQLite LIKE treats "\" literally until ESCAPE '\' is
    // added; it catches an escape implementation that is broken (e.g. ESCAPE added without escaping
    // the backslash itself, so "\" would escape the closing "%").
    [Theory]
    [InlineData("%25", "Pct% Alpha")]
    [InlineData("_", "Bee Under")]
    [InlineData("%5C", "Cee Back\\Slash")]
    public async Task ListContacts_SearchWithPercentUnderscoreBackslash_MatchesLiterally_AC033(
        string encodedTerm, string expected)
    {
        using var client = factory.CreateClient();
        await SeedAsync(
            client,
            ("Pct%", "Alpha", "a@x.test"), // literal % in first name
            ("Bee", "Under", "under_score@x.test"), // literal _ in email
            ("Cee", "Back\\Slash", "c@x.test"), // literal \ in last name
            ("Dee", "Plain", "d@x.test"), // decoys: a wildcard would match these
            ("Eve", "Other", "e@x.test"));

        var (names, total) = await ListAsync(client, $"?search={encodedTerm}");

        Assert.Equal(new[] { expected }, names);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task ListContacts_SearchTooLong_Returns400WithSearchError_AC034()
    {
        using var client = factory.CreateClient();
        await SeedFamousAsync(client);

        // 254 characters is accepted (no match, but 200), so the 400 below is about the length.
        var ok = await client.GetAsync($"/api/contacts?search={new string('a', 254)}", Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var response = await client.GetAsync($"/api/contacts?search={new string('a', 255)}", Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "search");
    }
}
