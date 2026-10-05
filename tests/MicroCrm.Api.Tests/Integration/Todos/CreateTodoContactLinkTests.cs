using System.Net;
using System.Text;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

namespace MicroCrm.Api.Tests.Integration.Todos;

// Create with a contact link (spec 003, T-04). Contact existence is decided by the FK (ADR-0008).
public sealed class CreateTodoContactLinkTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string GuidMessage = "Must be a valid GUID.";
    private const string MissingContactMessage = "Must refer to an existing contact.";
    private const string DateMessage = "Must be a valid date in YYYY-MM-DD format.";

    public static TheoryData<string> BlankContactBodies => new()
    {
        """{"title":"Unlinked"}""",
        """{"title":"Unlinked","contactId":null}""",
        """{"title":"Unlinked","contactId":""}""",
        """{"title":"Unlinked","contactId":"   "}""",
    };

    public static TheoryData<string> MalformedContactIds => new()
    {
        "abc",
        "123",
        "0f8fad5b-d9cb-469f-a165",
        "not a guid at all",
    };

    private async Task<long> CountAsync()
    {
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

    private async Task<Guid> NewContactAsync(string tag)
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        return await TodoStoreTests.CreateContactAsync(client, "Link", $"link.{tag}.{Guid.NewGuid():N}@example.com");
    }

    [Fact]
    public async Task CreateTodo_WithExistingContactId_LinksAndReturnsIt_AC007()
    {
        var contactId = await NewContactAsync("ac007");

        var response = await PostRawAsync($$"""{"title":"Call back","contactId":"{{contactId}}"}""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var id = created.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(contactId, created.RootElement.GetProperty("contactId").GetGuid());

        using var client = factory.CreateClient();
        var fetched = await client.GetAsync($"/api/todos/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        using var fetchedDoc = JsonDocument.Parse(await fetched.Content.ReadAsStringAsync(Ct));
        Assert.Equal(contactId, fetchedDoc.RootElement.GetProperty("contactId").GetGuid());

        var row = await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, id);
        Assert.NotNull(row);
        Assert.Equal(TodoStoreTests.Upper(contactId), row["ContactId"]);
    }

    [Theory]
    [InlineData("N")]
    [InlineData("B")]
    [InlineData("U")]
    public async Task CreateTodo_ContactIdInOtherGuidFormats_LinksAndReturnsCanonical_AC007(string format)
    {
        var contactId = await NewContactAsync("ac007fmt");
        var text = format == "U" ? contactId.ToString().ToUpperInvariant() : contactId.ToString(format);

        var response = await PostRawAsync($$"""{"title":"Formats","contactId":" {{text}} "}""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(contactId.ToString(), created.RootElement.GetProperty("contactId").GetString());
    }

    // Guard: a blank contactId means unlinked, and creating without a link never touches the contact table.
    [Theory]
    [MemberData(nameof(BlankContactBodies))]
    public async Task CreateTodo_ContactIdOmittedNullOrBlank_IsUnlinked_AC008(string body)
    {
        var response = await PostRawAsync(body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(JsonValueKind.Null, created.RootElement.GetProperty("contactId").ValueKind);
        var id = created.RootElement.GetProperty("id").GetGuid();
        var row = await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, id);
        Assert.NotNull(row);
        Assert.Null(row["ContactId"]);
    }

    [Theory]
    [MemberData(nameof(MalformedContactIds))]
    public async Task CreateTodo_MalformedContactId_Returns400ValidGuid_AC013(string contactId)
    {
        var before = await CountAsync();

        var response = await PostRawAsync(JsonSerializer.Serialize(new { title = "Bad link", contactId }));

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "contactId");
        Assert.Equal([GuidMessage], Messages(problem, "contactId"));
        Assert.Equal(before, await CountAsync());
    }

    [Fact]
    public async Task CreateTodo_UnknownContactId_Returns400MustReferToExistingContact_AC016()
    {
        var before = await CountAsync();

        var response = await PostRawAsync($$"""{"title":"Ghost link","contactId":"{{Guid.CreateVersion7()}}"}""");

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "contactId");
        Assert.Equal([MissingContactMessage], Messages(problem, "contactId"));
        Assert.Equal(before, await CountAsync());
    }

    // Guard: contact existence is checked only after the field rules pass, so only the field-rule entries are reported.
    [Fact]
    public async Task CreateTodo_FieldErrorAndUnknownContact_ReportsOnlyFieldErrors_AC017()
    {
        var before = await CountAsync();

        var response = await PostRawAsync(
            $$"""{"title":"  ","dueDate":"2026-02-30","contactId":"{{Guid.CreateVersion7()}}"}""");

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "title", "dueDate");
        Assert.Equal(["Required."], Messages(problem, "title"));
        Assert.Equal([DateMessage], Messages(problem, "dueDate"));
        Assert.Equal(before, await CountAsync());
    }

    [Fact]
    public async Task CreateTodo_InvalidFieldsIncludingContactId_ReportsAll_AC014()
    {
        var before = await CountAsync();

        var response = await PostRawAsync(
            $$"""{"title":"  ","notes":"{{new string('b', 4001)}}","dueDate":"2026-02-30","contactId":"abc"}""");

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "title", "notes", "dueDate", "contactId");
        Assert.Equal(["Required."], Messages(problem, "title"));
        Assert.Equal(["Must be 4000 characters or fewer."], Messages(problem, "notes"));
        Assert.Equal([DateMessage], Messages(problem, "dueDate"));
        Assert.Equal([GuidMessage], Messages(problem, "contactId"));
        Assert.Equal(before, await CountAsync());
    }

    [Theory]
    [InlineData("""{"title":"  ","contactId":"abc"}""", "title")]
    [InlineData("""{"title":"Fine","dueDate":"nope","contactId":"abc"}""", "dueDate")]
    public async Task CreateTodo_ContactIdPlusOneOtherInvalidField_ReportsBoth_AC014(string body, string other)
    {
        var response = await PostRawAsync(body);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, other, "contactId");
        Assert.Equal([GuidMessage], Messages(problem, "contactId"));
    }

    [Fact]
    public async Task CreateTodo_ContactIdMessages_FollowStyle_AC074()
    {
        var malformed = await PostRawAsync("""{"title":"Style","contactId":"abc"}""");
        using var malformedProblem = await ProblemAssert.IsProblemAsync(malformed, 400, "contactId");
        ProblemAssert.AssertValidationMessageStyle(malformedProblem);
        Assert.Equal([GuidMessage], Messages(malformedProblem, "contactId"));

        var unknown = await PostRawAsync($$"""{"title":"Style","contactId":"{{Guid.CreateVersion7()}}"}""");
        using var unknownProblem = await ProblemAssert.IsProblemAsync(unknown, 400, "contactId");
        ProblemAssert.AssertValidationMessageStyle(unknownProblem);
        Assert.Equal([MissingContactMessage], Messages(unknownProblem, "contactId"));
    }
}
