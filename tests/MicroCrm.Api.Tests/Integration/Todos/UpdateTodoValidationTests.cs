using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

namespace MicroCrm.Api.Tests.Integration.Todos;

// Update validation (spec 003, T-06). Each test seeds a to-do and, after the 400, proves it is unchanged
// (GET on a new client and a direct row read).
public sealed class UpdateTodoValidationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string DateMessage = "Must be a valid date in YYYY-MM-DD format.";
    private const string GuidMessage = "Must be a valid GUID.";

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
        """{"title":"x","contactId":7}""",
    };

    private sealed record Seeded(Guid Id, Guid ContactId, string Snapshot, Dictionary<string, object?> Row);

    private static StringContent Json(string raw) => new(raw, Encoding.UTF8, "application/json");

    private static string[] Messages(JsonDocument problem, string key) =>
        [.. problem.RootElement.GetProperty("errors").GetProperty(key).EnumerateArray().Select(e => e.GetString()!)];

    private async Task<Seeded> SeedAsync(string tag)
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var contactId = await TodoStoreTests.CreateContactAsync(client, "UpdVal", $"updval.{tag}.{Guid.NewGuid():N}@example.com");
        var response = await client.PostAsJsonAsync(
            "/api/todos",
            new { title = "Original", notes = "Original notes", dueDate = "2026-10-05", contactId },
            Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var id = doc.RootElement.GetProperty("id").GetGuid();
        var snapshot = await GetSnapshotAsync(id);
        var row = await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, id);
        Assert.NotNull(row);
        return new Seeded(id, contactId, snapshot, row);
    }

    private async Task<string> GetSnapshotAsync(Guid id)
    {
        using var fresh = factory.CreateClient();
        var response = await fresh.GetAsync($"/api/todos/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private async Task AssertUnchangedAsync(Seeded seeded)
    {
        Assert.Equal(seeded.Snapshot, await GetSnapshotAsync(seeded.Id));
        var row = await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, seeded.Id);
        Assert.NotNull(row);
        Assert.Equal(seeded.Row.Keys.Order().ToArray(), row.Keys.Order().ToArray());
        foreach (var (key, value) in seeded.Row)
        {
            Assert.Equal(value, row[key]);
        }
    }

    private async Task<HttpResponseMessage> PutRawAsync(Guid id, string raw)
    {
        using var client = factory.CreateClient();
        return await client.PutAsync($"/api/todos/{id}", Json(raw), Ct);
    }

    private Task<long> CountAsync() =>
        TodoStoreTests.ScalarAsync(factory.ConnectionString, "SELECT COUNT(*) FROM Todos");

    [Theory]
    [MemberData(nameof(MissingTitleBodies))]
    public async Task UpdateTodo_WithoutTitle_Returns400Required_AC010(string body)
    {
        var seeded = await SeedAsync("ac010");

        var response = await PutRawAsync(seeded.Id, body);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "title");
        Assert.Equal(["Required."], Messages(problem, "title"));
        await AssertUnchangedAsync(seeded);
    }

    [Fact]
    public async Task UpdateTodo_FieldOverMax_Returns400_AC011()
    {
        var seeded = await SeedAsync("ac011");

        var response = await PutRawAsync(
            seeded.Id, $$"""{"title":"{{new string('a', 201)}}","notes":"{{new string('b', 4001)}}"}""");

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "title", "notes");
        Assert.Equal(["Must be 200 characters or fewer."], Messages(problem, "title"));
        Assert.Equal(["Must be 4000 characters or fewer."], Messages(problem, "notes"));
        await AssertUnchangedAsync(seeded);
    }

    [Fact]
    public async Task UpdateTodo_OnlyNotesOverMax_ReportsOnlyNotes_AC011()
    {
        var seeded = await SeedAsync("ac011n");

        var response = await PutRawAsync(seeded.Id, $$"""{"title":"Fine","notes":"{{new string('b', 4001)}}"}""");

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "notes");
        Assert.Equal(["Must be 4000 characters or fewer."], Messages(problem, "notes"));
        await AssertUnchangedAsync(seeded);
    }

    [Theory]
    [MemberData(nameof(InvalidDueDates))]
    public async Task UpdateTodo_InvalidDueDate_Returns400_AC012(string dueDate)
    {
        var seeded = await SeedAsync("ac012");

        var response = await PutRawAsync(seeded.Id, JsonSerializer.Serialize(new { title = "Bad date", dueDate }));

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "dueDate");
        Assert.Equal([DateMessage], Messages(problem, "dueDate"));
        await AssertUnchangedAsync(seeded);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("123")]
    [InlineData("0f8fad5b-d9cb-469f-a165")]
    public async Task UpdateTodo_MalformedContactId_Returns400_AC013(string contactId)
    {
        var seeded = await SeedAsync("ac013");

        var response = await PutRawAsync(seeded.Id, JsonSerializer.Serialize(new { title = "Fine", contactId }));

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "contactId");
        Assert.Equal([GuidMessage], Messages(problem, "contactId"));
        await AssertUnchangedAsync(seeded);
    }

    [Fact]
    public async Task UpdateTodo_MultipleInvalidFields_ReportsAll_AC014()
    {
        var seeded = await SeedAsync("ac014");

        var response = await PutRawAsync(
            seeded.Id,
            $$"""{"title":"  ","notes":"{{new string('b', 4001)}}","dueDate":"2026-02-30","contactId":"abc"}""");

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "title", "notes", "dueDate", "contactId");
        Assert.Equal(["Required."], Messages(problem, "title"));
        Assert.Equal(["Must be 4000 characters or fewer."], Messages(problem, "notes"));
        Assert.Equal([DateMessage], Messages(problem, "dueDate"));
        Assert.Equal([GuidMessage], Messages(problem, "contactId"));
        await AssertUnchangedAsync(seeded);
    }

    // Guard: binding failures are already 400 problem+json before the handler runs.
    [Theory]
    [MemberData(nameof(MalformedBodies))]
    public async Task UpdateTodo_MalformedBody_Returns400Problem_AC015(string body)
    {
        var seeded = await SeedAsync("ac015");

        var response = await PutRawAsync(seeded.Id, body);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400);
        Assert.False(problem.RootElement.TryGetProperty("errors", out _), "Unreadable bodies carry no 'errors'");
        await AssertUnchangedAsync(seeded);
    }

    [Fact]
    public async Task UpdateTodo_InvalidBodyToUnknownId_Returns400_AC027()
    {
        _ = factory.Server;
        var unknown = Guid.CreateVersion7();
        var before = await CountAsync();

        var response = await PutRawAsync(
            unknown, $$"""{"title":" ","notes":"{{new string('b', 4001)}}","dueDate":"nope","contactId":"abc"}""");

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "title", "notes", "dueDate", "contactId");
        Assert.Equal(["Required."], Messages(problem, "title"));
        Assert.Equal(before, await CountAsync());
        Assert.Null(await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, unknown));
    }

    [Fact]
    public async Task UpdateTodo_ValidationMessages_FollowStyle_AC074()
    {
        var seeded = await SeedAsync("ac074");

        var response = await PutRawAsync(
            seeded.Id,
            $$"""{"title":"{{new string('a', 201)}}","notes":"{{new string('b', 4001)}}","dueDate":"05/10/2026","contactId":"abc"}""");

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "title", "notes", "dueDate", "contactId");
        ProblemAssert.AssertValidationMessageStyle(problem);
        Assert.Equal(["Must be 200 characters or fewer."], Messages(problem, "title"));
        Assert.Equal(["Must be 4000 characters or fewer."], Messages(problem, "notes"));
        Assert.Equal([DateMessage], Messages(problem, "dueDate"));
        Assert.Equal([GuidMessage], Messages(problem, "contactId"));

        var missing = await PutRawAsync(seeded.Id, """{"title":" "}""");
        using var missingProblem = await ProblemAssert.IsProblemAsync(missing, 400, "title");
        ProblemAssert.AssertValidationMessageStyle(missingProblem);
        Assert.Equal(["Required."], Messages(missingProblem, "title"));
        await AssertUnchangedAsync(seeded);
    }

    [Theory]
    [InlineData("""{"notes":"No title"}""")]
    [InlineData("""{"title":"  ","dueDate":"2026-02-30"}""")]
    [InlineData("""{"title":"Fine","notes":"NOTES_OVER","contactId":"abc"}""")]
    [InlineData("""{"title":"Fine","dueDate":"05/10/2026"}""")]
    public async Task UpdateTodo_SameInvalidPayload_SameErrorsAsCreate_NFR005(string payload)
    {
        var seeded = await SeedAsync("nfr005");
        var body = payload.Replace("NOTES_OVER", new string('b', 4001), StringComparison.Ordinal);
        using var client = factory.CreateClient();

        var created = await client.PostAsync("/api/todos", Json(body), Ct);
        var updated = await PutRawAsync(seeded.Id, body);

        using var createProblem = await ProblemAssert.IsProblemAsync(created, 400);
        using var updateProblem = await ProblemAssert.IsProblemAsync(updated, 400);
        var createErrors = createProblem.RootElement.GetProperty("errors");
        var updateErrors = updateProblem.RootElement.GetProperty("errors");
        Assert.Equal(
            createErrors.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray(),
            updateErrors.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        foreach (var property in createErrors.EnumerateObject())
        {
            Assert.Equal(
                property.Value.EnumerateArray().Select(e => e.GetString()).ToArray(),
                updateErrors.GetProperty(property.Name).EnumerateArray().Select(e => e.GetString()).ToArray());
        }

        await AssertUnchangedAsync(seeded);
    }
}
