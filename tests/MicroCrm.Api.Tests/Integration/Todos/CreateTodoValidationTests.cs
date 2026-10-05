using System.Net;
using System.Text;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

namespace MicroCrm.Api.Tests.Integration.Todos;

// Create validation (spec 003, T-03). Each test asserts the Todos row count is unchanged, read directly.
public sealed class CreateTodoValidationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string DateMessage = "Must be a valid date in YYYY-MM-DD format.";

    public static TheoryData<string> MissingTitleBodies => new()
    {
        """{"notes":"No title"}""",
        """{"title":null}""",
        """{"title":""}""",
        """{"title":"  "}""",
    };

    public static TheoryData<string> InvalidDueDates => new()
    {
        "2026-13-01",
        "2026-02-30",
        "05/10/2026",
        "2026-10-5",
        "2026-10-05T00:00:00Z",
        "226-10-05",
        "02026-10-05",
        "２０２６-10-05",
        "2026/10/05",
    };

    public static TheoryData<string> MalformedBodies => new()
    {
        "{",
        "",
        "null",
        "[]",
        """{"title":1}""",
        """{"title":"x","dueDate":5}""",
    };

    private async Task<long> CountAsync()
    {
        // Force the host to start and migrations to run, so the table exists.
        _ = factory.Server;
        return await TodoStoreTests.ScalarAsync(factory.ConnectionString, "SELECT COUNT(*) FROM Todos");
    }

    private async Task<HttpResponseMessage> PostRawAsync(string raw)
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        return await client.PostAsync("/api/todos", new StringContent(raw, Encoding.UTF8, "application/json"), Ct);
    }

    private static string[] Messages(JsonDocument problem, string key) =>
        [.. problem.RootElement.GetProperty("errors").GetProperty(key).EnumerateArray().Select(e => e.GetString()!)];

    [Theory]
    [MemberData(nameof(MissingTitleBodies))]
    public async Task CreateTodo_WithoutTitle_Returns400Required_AC010(string body)
    {
        var before = await CountAsync();

        var response = await PostRawAsync(body);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "title");
        Assert.Equal(["Required."], Messages(problem, "title"));
        Assert.Equal(before, await CountAsync());
    }

    [Fact]
    public async Task CreateTodo_FieldOverMax_Returns400_AC011()
    {
        var before = await CountAsync();

        var response = await PostRawAsync(
            $$"""{"title":"{{new string('a', 201)}}","notes":"{{new string('b', 4001)}}"}""");

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "title", "notes");
        Assert.Equal(["Must be 200 characters or fewer."], Messages(problem, "title"));
        Assert.Equal(["Must be 4000 characters or fewer."], Messages(problem, "notes"));
        Assert.Equal(before, await CountAsync());
    }

    [Fact]
    public async Task CreateTodo_OnlyNotesOverMax_ReportsOnlyNotes_AC011()
    {
        var before = await CountAsync();

        var response = await PostRawAsync($$"""{"title":"Fine","notes":"{{new string('b', 4001)}}"}""");

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "notes");
        Assert.Equal(["Must be 4000 characters or fewer."], Messages(problem, "notes"));
        Assert.Equal(before, await CountAsync());
    }

    [Theory]
    [MemberData(nameof(InvalidDueDates))]
    public async Task CreateTodo_InvalidDueDate_Returns400WithDueDateError_AC012(string dueDate)
    {
        var before = await CountAsync();
        var body = JsonSerializer.Serialize(new { title = "Bad date", dueDate });

        var response = await PostRawAsync(body);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "dueDate");
        Assert.Equal([DateMessage], Messages(problem, "dueDate"));
        Assert.Equal(before, await CountAsync());
    }

    [Fact]
    public async Task CreateTodo_MultipleInvalidFields_ReportsAllCamelCaseKeys_AC014()
    {
        var before = await CountAsync();

        var response = await PostRawAsync(
            $$"""{"title":"  ","notes":"{{new string('b', 4001)}}","dueDate":"2026-02-30"}""");

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "title", "notes", "dueDate");
        Assert.Equal(["Required."], Messages(problem, "title"));
        Assert.Equal(["Must be 4000 characters or fewer."], Messages(problem, "notes"));
        Assert.Equal([DateMessage], Messages(problem, "dueDate"));
        Assert.Equal(before, await CountAsync());
    }

    // Guard: binding failures may already return 400 problem+json before the handler runs.
    [Theory]
    [MemberData(nameof(MalformedBodies))]
    public async Task CreateTodo_MalformedBody_Returns400Problem_AC015(string body)
    {
        var before = await CountAsync();

        var response = await PostRawAsync(body);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400);
        Assert.False(problem.RootElement.TryGetProperty("errors", out _), "Unreadable bodies carry no 'errors'");
        Assert.Equal(before, await CountAsync());
    }

    [Fact]
    public async Task CreateTodo_ValidationMessages_FollowStyle_AC074()
    {
        var response = await PostRawAsync(
            $$"""{"title":"{{new string('a', 201)}}","notes":"{{new string('b', 4001)}}","dueDate":"05/10/2026"}""");

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "title", "notes", "dueDate");
        ProblemAssert.AssertValidationMessageStyle(problem);
        Assert.Equal(["Must be 200 characters or fewer."], Messages(problem, "title"));
        Assert.Equal(["Must be 4000 characters or fewer."], Messages(problem, "notes"));
        Assert.Equal([DateMessage], Messages(problem, "dueDate"));

        var missing = await PostRawAsync("""{"title":" "}""");
        using var missingProblem = await ProblemAssert.IsProblemAsync(missing, 400, "title");
        ProblemAssert.AssertValidationMessageStyle(missingProblem);
        Assert.Equal(["Required."], Messages(missingProblem, "title"));
    }

    [Fact]
    public async Task CreateTodo_Returns400AsProblemJson_AC072()
    {
        var response = await PostRawAsync("""{"title":""}""");

        using var problem = await ProblemAssert.IsProblemAsync(response, (int)HttpStatusCode.BadRequest);
        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
    }
}
