using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using MicroCrm.Api.Tests.Integration.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroCrm.Api.Tests.Integration.Todos;

// Create a to-do (spec 003, T-02). Each test uses its own data, so the shared class fixture needs no reset.
public sealed partial class CreateTodoTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] MemberNames =
        ["id", "title", "notes", "contactId", "dueDate", "isDone", "completedAt", "createdAt", "updatedAt"];

    public static TheoryData<string> BlankDueDateBodies => new()
    {
        """{"title":"Blank due"}""",
        """{"title":"Blank due","dueDate":null}""",
        """{"title":"Blank due","dueDate":""}""",
        """{"title":"Blank due","dueDate":"   "}""",
    };

    private static StringContent Json(string raw) => new(raw, Encoding.UTF8, "application/json");

    private static async Task<(HttpResponseMessage Response, JsonDocument Doc)> PostAsync(HttpClient client, string raw)
    {
        var response = await client.PostAsync("/api/todos", Json(raw), Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        return (response, JsonDocument.Parse(body));
    }

    [Fact]
    public async Task CreateTodo_WithTitleNotesAndDueDate_Returns201WithLocationAndBody_AC001()
    {
        using var client = factory.CreateClient();

        var (response, doc) = await PostAsync(client, """{"title":"Call Ada","notes":"About the quote","dueDate":"2026-10-05"}""");
        using var scope = doc;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var root = doc.RootElement;
        foreach (var name in MemberNames)
        {
            Assert.True(root.TryGetProperty(name, out _), $"Missing member '{name}'");
        }

        var id = root.GetProperty("id").GetGuid();
        Assert.NotNull(response.Headers.Location);
        var locationPath = response.Headers.Location.IsAbsoluteUri
            ? response.Headers.Location.AbsolutePath
            : response.Headers.Location.OriginalString;
        Assert.Equal($"/api/todos/{id}", locationPath, ignoreCase: true);
        Assert.Equal("Call Ada", root.GetProperty("title").GetString());
        Assert.Equal("About the quote", root.GetProperty("notes").GetString());
        Assert.Equal("2026-10-05", root.GetProperty("dueDate").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("contactId").ValueKind);
        Assert.False(root.GetProperty("isDone").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("completedAt").ValueKind);
    }

    [Fact]
    public async Task CreateTodo_ThenGet_ReturnsSameValuesOnNewConnections_AC001()
    {
        using var client = factory.CreateClient();
        var (created, createdDoc) = await PostAsync(client, """{"title":"Persisted","notes":"Kept","dueDate":"2027-03-04"}""");
        using var scope = createdDoc;
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = createdDoc.RootElement.GetProperty("id").GetGuid();

        using var other = factory.CreateClient();
        var response = await other.GetAsync($"/api/todos/{id}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(createdDoc.RootElement.GetRawText(), (await response.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task CreateTodo_WithOnlyTitle_ReturnsNullsAndIsDoneFalse_AC002()
    {
        using var client = factory.CreateClient();

        var (response, doc) = await PostAsync(client, """{"title":"Only title"}""");
        using var scope = doc;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var root = doc.RootElement;
        foreach (var name in new[] { "notes", "contactId", "dueDate", "completedAt" })
        {
            Assert.True(root.TryGetProperty(name, out var value), $"Member '{name}' must be present");
            Assert.Equal(JsonValueKind.Null, value.ValueKind);
        }

        Assert.Equal(JsonValueKind.False, root.GetProperty("isDone").ValueKind);
    }

    [Fact]
    public async Task CreateTodo_IgnoresBodyIdTimestampsAndDoneState_AC003()
    {
        using var client = factory.CreateClient();
        var bogusId = Guid.CreateVersion7();
        var body = $$"""
            {"title":"Server owned","id":"{{bogusId}}","createdAt":"2000-01-01T00:00:00Z","updatedAt":"2001-01-01T00:00:00Z","isDone":true,"completedAt":"2002-01-01T00:00:00Z"}
            """;

        var (first, firstDoc) = await PostAsync(client, body);
        using var scope1 = firstDoc;
        var (second, secondDoc) = await PostAsync(client, body);
        using var scope2 = secondDoc;

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var root = firstDoc.RootElement;
        var id = root.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, id);
        Assert.NotEqual(bogusId, id);
        Assert.NotEqual(id, secondDoc.RootElement.GetProperty("id").GetGuid());
        Assert.False(root.GetProperty("isDone").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("completedAt").ValueKind);
        var now = factory.Time.GetUtcNow();
        Assert.Equal(now, root.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(now, root.GetProperty("updatedAt").GetDateTimeOffset());
    }

    [Theory]
    [InlineData("  Call Ada\t", "  Some notes \n", "Call Ada", "Some notes")]
    [InlineData("Plain", "", "Plain", null)]
    [InlineData("Plain", "   ", "Plain", null)]
    [InlineData("\n Plain  ", "\t \n", "Plain", null)]
    public async Task CreateTodo_WithSurroundingWhitespace_StoresTrimmed_AC004(string title, string notes, string expectedTitle, string? expectedNotes)
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/todos", new { title, notes }, Ct);
        var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        using var scope = created;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = created.RootElement.GetProperty("id").GetGuid();
        using var other = factory.CreateClient();
        using var fetched = JsonDocument.Parse(await other.GetStringAsync($"/api/todos/{id}", Ct));
        foreach (var root in new[] { created.RootElement, fetched.RootElement })
        {
            Assert.Equal(expectedTitle, root.GetProperty("title").GetString());
            Assert.Equal(expectedNotes, root.GetProperty("notes").GetString());
        }
    }

    [Theory]
    [InlineData("0001-01-01")]
    [InlineData("9999-12-31")]
    [InlineData("2024-02-29")]
    [InlineData("2026-10-05")]
    [InlineData(" 2026-10-05 ")]
    public async Task CreateTodo_WithValidDueDate_ReturnsSameString_AC005(string dueDate)
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/todos", new { title = "Due", dueDate }, Ct);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var expected = dueDate.Trim();
        Assert.Equal(expected, created.RootElement.GetProperty("dueDate").GetString());
        var id = created.RootElement.GetProperty("id").GetGuid();
        using var other = factory.CreateClient();
        using var fetched = JsonDocument.Parse(await other.GetStringAsync($"/api/todos/{id}", Ct));
        Assert.Equal(expected, fetched.RootElement.GetProperty("dueDate").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateTodo_WithDueDateTodayOrYesterday_ReturnsSameString_AC005(int dayOffset)
    {
        using var client = factory.CreateClient();
        var dueDate = DateOnly.FromDateTime(factory.Time.GetUtcNow().UtcDateTime).AddDays(dayOffset).ToString("yyyy-MM-dd");

        var response = await client.PostAsJsonAsync("/api/todos", new { title = "Relative due", dueDate }, Ct);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(dueDate, created.RootElement.GetProperty("dueDate").GetString());
    }

    [Theory]
    [MemberData(nameof(BlankDueDateBodies))]
    public async Task CreateTodo_DueDateOmittedNullOrBlank_IsNull_AC006(string body)
    {
        using var client = factory.CreateClient();

        var (response, doc) = await PostAsync(client, body);
        using var scope = doc;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(doc.RootElement.TryGetProperty("dueDate", out var dueDate), "dueDate must be present");
        Assert.Equal(JsonValueKind.Null, dueDate.ValueKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateTodo_FieldsAtMax_Returns201_AC009(bool wrapInWhitespace)
    {
        using var client = factory.CreateClient();
        var title = new string('t', 200);
        var notes = new string('n', 4000);
        var sentTitle = wrapInWhitespace ? $"  {title}\t" : title;
        var sentNotes = wrapInWhitespace ? $"\n {notes}  " : notes;

        var response = await client.PostAsJsonAsync("/api/todos", new { title = sentTitle, notes = sentNotes }, Ct);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(title, created.RootElement.GetProperty("title").GetString());
        Assert.Equal(notes, created.RootElement.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task CreateTodo_Json_IsCamelCaseWithNullsDateOnlyAndUtcOffsets_NFR002()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/todos", Json("""{"title":"Raw json","dueDate":"2026-10-05"}"""), Ct);
        var raw = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("\"dueDate\":\"2026-10-05\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"isDone\":false", raw, StringComparison.Ordinal);
        Assert.Contains("\"notes\":null", raw, StringComparison.Ordinal);
        Assert.Contains("\"contactId\":null", raw, StringComparison.Ordinal);
        Assert.Contains("\"completedAt\":null", raw, StringComparison.Ordinal);
        Assert.Matches(UtcTimestamp("createdAt"), raw);
        Assert.Matches(UtcTimestamp("updatedAt"), raw);
        Assert.DoesNotContain("\"Title\"", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("\"IsDone\"", raw, StringComparison.Ordinal);
    }

    // Guard (NFR-004): a regression that logs title or notes at Information or above must fail this test.
    // Asserts 201 first so the check is never vacuous.
    [Fact]
    public async Task CreateTodo_TitleAndNotesNotLoggedAtInformationOrAbove_NFR004()
    {
        var titleMarker = $"nfr004-title-{Guid.NewGuid():N}";
        var notesMarker = $"nfr004-notes-{Guid.NewGuid():N}";
        var logs = new CapturingLoggerProvider();
        using var logged = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(logs)));
        using var client = logged.CreateClient();

        var response = await client.PostAsJsonAsync("/api/todos", new { title = titleMarker, notes = notesMarker }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        // Canary: capture is live and EF's command log (where a value could leak) is at Information.
        Assert.Contains(logs.Entries, e => e.Category == "Microsoft.EntityFrameworkCore.Database.Command" && e.Level >= LogLevel.Information);
        foreach (var (category, level, text) in logs.Entries.Where(e => e.Level >= LogLevel.Information))
        {
            Assert.False(text.Contains(titleMarker, StringComparison.OrdinalIgnoreCase), $"Title appeared in a {level} log entry from {category}.");
            Assert.False(text.Contains(notesMarker, StringComparison.OrdinalIgnoreCase), $"Notes appeared in a {level} log entry from {category}.");
        }
    }

    private static Regex UtcTimestamp(string member) =>
        new($"\"{member}\":\"[0-9T:.\\-]+(Z|\\+00:00)\"", RegexOptions.CultureInvariant);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(string Category, LogLevel Level, string Text)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentQueue<(string, LogLevel, string)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var text = formatter(state, exception) + "\n" + state + "\n" + exception;
                entries.Enqueue((category, logLevel, text));
            }
        }
    }
}
