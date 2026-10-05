using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

namespace MicroCrm.Api.Tests.Integration.Contacts;

public sealed class ListContactsPagingTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Creates contacts in the given order, advancing the clock between creates so that
    // GUID v7 ids ascend in insertion order. Returns the created ids in insertion order.
    private async Task<List<Guid>> SeedAsync(HttpClient client, params (string First, string Last)[] seeds)
    {
        var ids = new List<Guid>();
        var n = 0;
        foreach (var (first, last) in seeds)
        {
            factory.Time.Advance(TimeSpan.FromMilliseconds(5));
            var response = await client.PostAsJsonAsync(
                "/api/contacts",
                new { firstName = first, lastName = last, email = $"paging{n++}@example.com" },
                Ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
            ids.Add(doc.RootElement.GetProperty("id").GetGuid());
        }

        return ids;
    }

    private static async Task<JsonDocument> GetAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync("/api/contacts" + query, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task ListContacts_PageAndPageSize_ReturnsSliceAndEchoesParams_AC024()
    {
        using var client = factory.CreateClient();

        // Seeded out of sorted order; sorted by last name this is A..G.
        await SeedAsync(
            client,
            ("Ann", "Gamma"), ("Ann", "Bravo"), ("Ann", "Echo"), ("Ann", "Alpha"),
            ("Ann", "Foxtrot"), ("Ann", "Charlie"), ("Ann", "Delta"));

        using var doc = await GetAsync(client, "?page=2&pageSize=3");

        var root = doc.RootElement;
        Assert.Equal(2, root.GetProperty("page").GetInt32());
        Assert.Equal(3, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(7, root.GetProperty("totalCount").GetInt32());
        var lastNames = root.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("lastName").GetString()).ToArray();
        Assert.Equal(new[] { "Delta", "Echo", "Foxtrot" }, lastNames);
    }

    [Fact]
    public async Task ListContacts_PagingThroughAll_ReturnsEachContactOnce_AC025()
    {
        using var client = factory.CreateClient();

        // Several contacts share identical first and last names. This checks no duplicates or gaps across pages;
        // the id tie-break itself is pinned by AC022/AC023 (rowid order equals id order, so it cannot fail here).
        var seeded = await SeedAsync(
            client,
            ("Sam", "Smith"), ("Zed", "Young"), ("Sam", "Smith"), ("Amy", "Smith"),
            ("Sam", "Smith"), ("Zed", "Young"), ("Sam", "Smith"));

        var seen = new List<Guid>();
        var maxPages = (seeded.Count / 3) + 2;
        var finished = false;
        for (var page = 1; page <= maxPages; page++)
        {
            using var doc = await GetAsync(client, $"?page={page}&pageSize=3");
            Assert.Equal(page, doc.RootElement.GetProperty("page").GetInt32());
            Assert.Equal(3, doc.RootElement.GetProperty("pageSize").GetInt32());
            Assert.Equal(seeded.Count, doc.RootElement.GetProperty("totalCount").GetInt32());
            var items = doc.RootElement.GetProperty("items").EnumerateArray().ToArray();
            Assert.True(items.Length <= 3, $"page {page} returned {items.Length} items for pageSize=3");
            seen.AddRange(items.Select(i => i.GetProperty("id").GetGuid()));
            if (items.Length < 3)
            {
                finished = true;
                break;
            }
        }

        Assert.True(finished, "paging did not end with a short or empty page");
        Assert.Equal(seen.Count, seen.Distinct().Count()); // no duplicates
        Assert.Equal(seeded.Order().ToArray(), seen.Order().ToArray()); // no gaps
    }

    [Fact]
    public async Task ListContacts_PageBeyondLast_ReturnsEmptyItemsWithTotal_AC026()
    {
        using var client = factory.CreateClient();
        await SeedAsync(client, ("A", "One"), ("B", "Two"), ("C", "Three"), ("D", "Four"), ("E", "Five"));

        // page=2147483647 (int.MaxValue) guards against (page - 1) * pageSize overflowing int.
        foreach (var query in new[]
                 {
                     "?page=3&pageSize=3",
                     "?page=2147483647",
                     "?page=2147483647&pageSize=3",
                     "?page=2147483647&pageSize=100",
                 })
        {
            using var doc = await GetAsync(client, query);

            var items = doc.RootElement.GetProperty("items");
            Assert.Equal(JsonValueKind.Array, items.ValueKind);
            Assert.True(items.GetArrayLength() == 0, $"{query} returned {items.GetArrayLength()} items");
            Assert.Equal(5, doc.RootElement.GetProperty("totalCount").GetInt32());
        }
    }

    [Fact]
    public async Task ListContacts_PageSize100_ReturnsUpTo100_AC027()
    {
        using var client = factory.CreateClient();

        // Bulk insert 101 rows directly, in the stored formats (uppercase TEXT ids, EF DateTimeOffset text).
        await factory.ExecuteSqlAsync(
            "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 101) " +
            "INSERT INTO Contacts (Id, FirstName, LastName, Email, CreatedAt, UpdatedAt) " +
            "SELECT printf('00000000-0000-7000-8000-%012X', i), 'First' || i, 'Last' || printf('%03d', i), " +
            "'bulk' || i || '@example.com', '2026-01-01 00:00:00+00:00', '2026-01-01 00:00:00+00:00' FROM n");

        using var doc = await GetAsync(client, "?pageSize=100");

        Assert.Equal(100, doc.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(101, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(100, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    // " 1" and "+1" kill the NumberStyles.None -> default int.TryParse mutant; "0"/"-1" the removed min check;
    // "101" the removed max check.
    [Theory]
    [InlineData("page", "0")]
    [InlineData("page", "-1")]
    [InlineData("page", "abc")]
    [InlineData("page", "1.5")]
    [InlineData("page", "")]
    [InlineData("page", "2147483648")]
    [InlineData("page", " 1")]
    [InlineData("page", "+1")]
    [InlineData("pageSize", "0")]
    [InlineData("pageSize", "101")]
    [InlineData("pageSize", "-1")]
    [InlineData("pageSize", "abc")]
    [InlineData("pageSize", " 1")]
    [InlineData("pageSize", "+1")]
    [InlineData("pageSize", "")]
    [InlineData("page", "  ")]
    public async Task ListContacts_InvalidPaging_Returns400WithParamError_AC028(string parameter, string value)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/contacts?{parameter}={Uri.EscapeDataString(value)}", Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, parameter);
    }

    [Fact]
    public async Task ListContacts_BothInvalid_ReportsBoth_AC028()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/contacts?page=0&pageSize=101", Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "page", "pageSize");
    }

    [Fact]
    public async Task ListContacts_ValidationMessages_FollowStyle_AC039()
    {
        using var client = factory.CreateClient();
        var search = new string('a', 255);

        var response = await client.GetAsync($"/api/contacts?page=0&pageSize=101&search={search}", Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "page", "pageSize", "search");
        ProblemAssert.AssertValidationMessageStyle(problem);
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["Must be an integer between 1 and 2147483647."], errors.GetProperty("page").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Equal(["Must be an integer between 1 and 100."], errors.GetProperty("pageSize").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Equal(["Must be 254 characters or fewer."], errors.GetProperty("search").EnumerateArray().Select(e => e.GetString()).ToArray());
    }
}
