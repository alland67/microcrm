using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

namespace MicroCrm.Api.Tests.Integration.Todos;

// List to-dos: paging (spec 003, T-10). Each test starts from an empty store.
public sealed class ListTodosPagingTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Creates to-dos titled "T0".. with ascending due dates (2026-06-01 + i), so the list order is insertion order.
    private static async Task<List<Guid>> SeedAsync(HttpClient client, int count)
    {
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var due = new DateOnly(2026, 6, 1).AddDays(i).ToString("yyyy-MM-dd");
            var response = await client.PostAsJsonAsync("/api/todos", new { title = $"T{i}", dueDate = due }, Ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
            ids.Add(doc.RootElement.GetProperty("id").GetGuid());
        }

        return ids;
    }

    private static async Task<JsonDocument> GetOkAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync("/api/todos" + query, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
    }

    private static Guid[] Ids(JsonDocument doc) =>
        doc.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToArray();

    private static string[] Messages(JsonElement errors, string key) =>
        errors.GetProperty(key).EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Fact]
    public async Task ListTodos_PageSlices_EchoPagingAndTotal_AC045()
    {
        using var client = factory.CreateClient();
        var ids = await SeedAsync(client, 7);

        using var first = await GetOkAsync(client, "?page=1&pageSize=3");
        using var second = await GetOkAsync(client, "?page=2&pageSize=3");
        using var third = await GetOkAsync(client, "?page=3&pageSize=3");

        Assert.Equal(ids.Take(3), Ids(first));
        Assert.Equal(ids.Skip(3).Take(3), Ids(second));
        Assert.Equal(ids.Skip(6), Ids(third));
        foreach (var (doc, page) in new[] { (first, 1), (second, 2), (third, 3) })
        {
            Assert.Equal(page, doc.RootElement.GetProperty("page").GetInt32());
            Assert.Equal(3, doc.RootElement.GetProperty("pageSize").GetInt32());
            Assert.Equal(7, doc.RootElement.GetProperty("totalCount").GetInt32());
        }
    }

    [Fact]
    public async Task ListTodos_PageBeyondLast_ReturnsEmptyItems_AC045()
    {
        using var client = factory.CreateClient();
        await SeedAsync(client, 5);

        // int.MaxValue guards against (page - 1) * pageSize overflowing int.
        foreach (var query in new[]
                 {
                     "?page=3&pageSize=3",
                     "?page=2147483647",
                     "?page=2147483647&pageSize=3",
                     "?page=2147483647&pageSize=100",
                 })
        {
            using var doc = await GetOkAsync(client, query);

            Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("items").ValueKind);
            Assert.True(doc.RootElement.GetProperty("items").GetArrayLength() == 0, $"{query} returned items");
            Assert.Equal(5, doc.RootElement.GetProperty("totalCount").GetInt32());
        }
    }

    [Fact]
    public async Task ListTodos_PageSize100_IsAccepted_AC045()
    {
        using var client = factory.CreateClient();
        var ids = await SeedAsync(client, 101);

        using var doc = await GetOkAsync(client, "?pageSize=100");

        Assert.Equal(100, doc.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(100, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(101, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(ids.Take(100), Ids(doc));
    }

    [Fact]
    public async Task ListTodos_AllPages_ReturnEveryTodoExactlyOnceWithTies_AC045()
    {
        using var client = factory.CreateClient();
        var seeded = new List<Guid>();

        // 5 dated and 5 undated to-dos created at the same clock instant: all ties on date and createdAt.
        for (var i = 0; i < 10; i++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/todos", new { title = $"Tie {i}", dueDate = i < 5 ? "2026-07-01" : null }, Ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
            seeded.Add(created.RootElement.GetProperty("id").GetGuid());
        }

        var seen = new List<Guid>();
        var finished = false;
        for (var page = 1; page <= 6; page++)
        {
            using var doc = await GetOkAsync(client, $"?page={page}&pageSize=3");
            Assert.Equal(10, doc.RootElement.GetProperty("totalCount").GetInt32());
            var pageIds = Ids(doc);
            Assert.True(pageIds.Length <= 3);
            seen.AddRange(pageIds);
            if (pageIds.Length < 3)
            {
                finished = true;
                break;
            }
        }

        Assert.True(finished, "paging did not end with a short page");
        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Equal(seeded.Order().ToArray(), seen.Order().ToArray());

        // Same order as one big page.
        using var all = await GetOkAsync(client, "?pageSize=100");
        Assert.Equal(Ids(all), seen);
    }

    [Theory]
    [InlineData("page", "0")]
    [InlineData("page", "-1")]
    [InlineData("page", "abc")]
    [InlineData("page", "1.5")]
    [InlineData("page", "")]
    [InlineData("page", "  ")]
    [InlineData("page", "2147483648")]
    [InlineData("page", " 1")]
    [InlineData("page", "+1")]
    [InlineData("pageSize", "0")]
    [InlineData("pageSize", "101")]
    [InlineData("pageSize", "-1")]
    [InlineData("pageSize", "abc")]
    [InlineData("pageSize", "1.5")]
    [InlineData("pageSize", "")]
    [InlineData("pageSize", " 1")]
    [InlineData("pageSize", "+1")]
    public async Task ListTodos_InvalidPageOrPageSize_Returns400_AC046(string parameter, string value)
    {
        using var client = factory.CreateClient();
        await SeedAsync(client, 1);

        var response = await client.GetAsync($"/api/todos?{parameter}={Uri.EscapeDataString(value)}", Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, parameter);
        var expected = parameter == "page"
            ? "Must be an integer between 1 and 2147483647."
            : "Must be an integer between 1 and 100.";
        Assert.Equal([expected], Messages(problem.RootElement.GetProperty("errors"), parameter));
    }

    [Fact]
    public async Task ListTodos_InvalidPageAndPageSize_ReportsBoth_AC056()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/todos?page=0&pageSize=101", Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "page", "pageSize");
    }

    [Fact]
    public async Task ListTodos_ValidationMessages_FollowStyle_AC074()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/todos?page=0&pageSize=0", Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "page", "pageSize");
        ProblemAssert.AssertValidationMessageStyle(problem);
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["Must be an integer between 1 and 2147483647."], Messages(errors, "page"));
        Assert.Equal(["Must be an integer between 1 and 100."], Messages(errors, "pageSize"));
    }
}
