using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

namespace MicroCrm.Api.Tests.Integration.Todos;

// Update a to-do, full replace (spec 003, T-05). Each test uses its own data, so the shared class fixture needs no reset.
public sealed class UpdateTodoTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] MemberNames =
        ["id", "title", "notes", "contactId", "dueDate", "isDone", "completedAt", "createdAt", "updatedAt"];

    public static TheoryData<string, string> BlankVariants()
    {
        var data = new TheoryData<string, string>();
        foreach (var field in new[] { "notes", "dueDate", "contactId" })
        {
            foreach (var variant in new[] { "omitted", "null", "empty", "whitespace" })
            {
                data.Add(field, variant);
            }
        }

        return data;
    }

    private static StringContent Json(string raw) => new(raw, Encoding.UTF8, "application/json");

    private static async Task<Guid> NewContactAsync(HttpClient client, string tag) =>
        await TodoStoreTests.CreateContactAsync(client, "Upd", $"upd.{tag}.{Guid.NewGuid():N}@example.com");

    private static async Task<Guid> CreateTodoAsync(
        HttpClient client, string title, Guid? contactId = null, string? notes = null, string? dueDate = null)
    {
        var response = await client.PostAsJsonAsync("/api/todos", new { title, notes, dueDate, contactId }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, string raw) =>
        await client.PutAsync($"/api/todos/{id}", Json(raw), Ct);

    private static async Task<string> GetBodyAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private static async Task<JsonDocument> OkBodyAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
    }

    private static string? Str(JsonElement root, string name) =>
        root.GetProperty(name).ValueKind == JsonValueKind.Null ? null : root.GetProperty(name).GetString();

    [Fact]
    public async Task UpdateTodo_WithValidBody_Returns200WithUpdatedTodo_AC020()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var contactId = await NewContactAsync(client, "ac020");
        var id = await CreateTodoAsync(client, "Before", null, "Old notes", "2026-01-01");

        var response = await PutAsync(client, id,
            $$"""{"title":"After","notes":"New notes","dueDate":"2026-10-05","contactId":"{{contactId}}"}""");

        using var doc = await OkBodyAsync(response);
        var root = doc.RootElement;
        foreach (var name in MemberNames)
        {
            Assert.True(root.TryGetProperty(name, out _), $"Missing member '{name}'");
        }

        Assert.Equal(id, root.GetProperty("id").GetGuid());
        Assert.Equal("After", root.GetProperty("title").GetString());
        Assert.Equal("New notes", root.GetProperty("notes").GetString());
        Assert.Equal("2026-10-05", root.GetProperty("dueDate").GetString());
        Assert.Equal(contactId, root.GetProperty("contactId").GetGuid());
        Assert.False(root.GetProperty("isDone").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("completedAt").ValueKind);
    }

    [Fact]
    public async Task UpdateTodo_ThenGet_ReturnsPersistedValuesOnNewConnections_AC020()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var contactId = await NewContactAsync(client, "ac020get");
        var id = await CreateTodoAsync(client, "Before");

        var put = await PutAsync(client, id,
            $$"""{"title":"Persisted","notes":"N","dueDate":"2027-02-03","contactId":"{{contactId}}"}""");
        var putBody = await put.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        using var fresh = factory.CreateClient();
        var fetched = await GetBodyAsync(fresh, $"/api/todos/{id}");
        using var putDoc = JsonDocument.Parse(putBody);
        using var getDoc = JsonDocument.Parse(fetched);
        Assert.Equal("Persisted", getDoc.RootElement.GetProperty("title").GetString());
        Assert.Equal("N", getDoc.RootElement.GetProperty("notes").GetString());
        Assert.Equal("2027-02-03", getDoc.RootElement.GetProperty("dueDate").GetString());
        Assert.Equal(contactId, getDoc.RootElement.GetProperty("contactId").GetGuid());
        Assert.Equal(putDoc.RootElement.GetRawText(), getDoc.RootElement.GetRawText());

        var row = await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, id);
        Assert.NotNull(row);
        Assert.Equal("Persisted", row["Title"]);
        Assert.Equal(TodoStoreTests.Upper(contactId), row["ContactId"]);
    }

    [Theory]
    [MemberData(nameof(BlankVariants))]
    public async Task UpdateTodo_OptionalFieldOmittedNullOrBlank_StoredAsNull_AC021(string field, string variant)
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var contactId = await NewContactAsync(client, "ac021");
        var id = await CreateTodoAsync(client, "Has values", contactId, "Some notes", "2026-10-05");
        var original = new Dictionary<string, string>
        {
            ["notes"] = "Some notes",
            ["dueDate"] = "2026-10-05",
            ["contactId"] = contactId.ToString(),
        };

        var members = new List<string> { "\"title\":\"Has values\"" };
        foreach (var (name, value) in original)
        {
            if (name != field)
            {
                members.Add($"\"{name}\":\"{value}\"");
                continue;
            }

            switch (variant)
            {
                case "null": members.Add($"\"{name}\":null"); break;
                case "empty": members.Add($"\"{name}\":\"\""); break;
                case "whitespace": members.Add($"\"{name}\":\"   \""); break;
            }
        }

        var response = await PutAsync(client, id, "{" + string.Join(",", members) + "}");

        using var doc = await OkBodyAsync(response);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty(field).ValueKind);
        foreach (var (name, value) in original.Where(kv => kv.Key != field))
        {
            Assert.Equal(value, doc.RootElement.GetProperty(name).GetString());
        }

        using var fresh = factory.CreateClient();
        using var fetched = JsonDocument.Parse(await GetBodyAsync(fresh, $"/api/todos/{id}"));
        Assert.Equal(JsonValueKind.Null, fetched.RootElement.GetProperty(field).ValueKind);
        var row = await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, id);
        Assert.NotNull(row);
        Assert.Null(row[char.ToUpperInvariant(field[0]) + field[1..]]);
    }

    [Fact]
    public async Task UpdateTodo_IgnoresBodyIdTimestampsAndDoneState_AC022()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var id = await CreateTodoAsync(client, "Original");
        using var before = JsonDocument.Parse(await GetBodyAsync(client, $"/api/todos/{id}"));
        var createdAt = before.RootElement.GetProperty("createdAt").GetDateTimeOffset();
        factory.Time.Advance(TimeSpan.FromHours(1));
        var otherId = Guid.CreateVersion7();

        var response = await PutAsync(client, id, $$"""
            {"id":"{{otherId}}","title":"Renamed","createdAt":"1999-01-01T00:00:00+00:00",
             "updatedAt":"1999-01-02T00:00:00+00:00","isDone":true,"completedAt":"1999-01-03T00:00:00+00:00"}
            """);

        using var doc = await OkBodyAsync(response);
        using var fresh = factory.CreateClient();
        using var fetched = JsonDocument.Parse(await GetBodyAsync(fresh, $"/api/todos/{id}"));
        foreach (var root in new[] { doc.RootElement, fetched.RootElement })
        {
            Assert.Equal(id, root.GetProperty("id").GetGuid());
            Assert.Equal("Renamed", root.GetProperty("title").GetString());
            Assert.Equal(createdAt, root.GetProperty("createdAt").GetDateTimeOffset());
            Assert.False(root.GetProperty("isDone").GetBoolean());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("completedAt").ValueKind);
            Assert.Equal(factory.Time.GetUtcNow(), root.GetProperty("updatedAt").GetDateTimeOffset());
        }

        Assert.Null(await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, otherId));
    }

    [Fact]
    public async Task UpdateTodo_SetsUpdatedAtFromClock_EvenWhenUnchanged_AC023()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var id = await CreateTodoAsync(client, "Same", null, "Same notes", "2026-10-05");
        using var before = JsonDocument.Parse(await GetBodyAsync(client, $"/api/todos/{id}"));
        var createdAt = before.RootElement.GetProperty("createdAt").GetDateTimeOffset();
        Assert.Equal(createdAt, before.RootElement.GetProperty("updatedAt").GetDateTimeOffset());
        factory.Time.Advance(TimeSpan.FromHours(2));

        var response = await PutAsync(client, id, """{"title":"Same","notes":"Same notes","dueDate":"2026-10-05"}""");

        using var doc = await OkBodyAsync(response);
        Assert.Equal(factory.Time.GetUtcNow(), doc.RootElement.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.Equal(createdAt, doc.RootElement.GetProperty("createdAt").GetDateTimeOffset());
        Assert.NotEqual(createdAt, doc.RootElement.GetProperty("updatedAt").GetDateTimeOffset());

        using var fresh = factory.CreateClient();
        using var fetched = JsonDocument.Parse(await GetBodyAsync(fresh, $"/api/todos/{id}"));
        Assert.Equal(factory.Time.GetUtcNow(), fetched.RootElement.GetProperty("updatedAt").GetDateTimeOffset());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateTodo_LinksToContact_FromUnlinkedOrOtherContact_AC024(bool previouslyLinkedToOther)
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var target = await NewContactAsync(client, "ac024t");
        Guid? previous = previouslyLinkedToOther ? await NewContactAsync(client, "ac024p") : null;
        var id = await CreateTodoAsync(client, "Relink", previous);
        using var before = JsonDocument.Parse(await GetBodyAsync(client, $"/api/todos/{id}"));
        Assert.Equal(previous?.ToString(), Str(before.RootElement, "contactId"));

        var response = await PutAsync(client, id, $$"""{"title":"Relink","contactId":"{{target}}"}""");

        using var doc = await OkBodyAsync(response);
        Assert.Equal(target, doc.RootElement.GetProperty("contactId").GetGuid());
        using var fresh = factory.CreateClient();
        using var fetched = JsonDocument.Parse(await GetBodyAsync(fresh, $"/api/todos/{id}"));
        Assert.Equal(target, fetched.RootElement.GetProperty("contactId").GetGuid());
        var row = await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, id);
        Assert.NotNull(row);
        Assert.Equal(TodoStoreTests.Upper(target), row["ContactId"]);
    }

    [Fact]
    public async Task UpdateTodo_AtMaxWithWhitespace_StoresTrimmed_AC025()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var id = await CreateTodoAsync(client, "Short");
        var title = new string('t', 200);
        var notes = new string('n', 4000);

        var response = await PutAsync(client, id, JsonSerializer.Serialize(new
        {
            title = $"  {title}\t",
            notes = $"\n{notes}  ",
            dueDate = " 2026-10-05 ",
        }));

        using var doc = await OkBodyAsync(response);
        Assert.Equal(title, doc.RootElement.GetProperty("title").GetString());
        Assert.Equal(notes, doc.RootElement.GetProperty("notes").GetString());
        Assert.Equal("2026-10-05", doc.RootElement.GetProperty("dueDate").GetString());

        using var fresh = factory.CreateClient();
        using var fetched = JsonDocument.Parse(await GetBodyAsync(fresh, $"/api/todos/{id}"));
        Assert.Equal(title, fetched.RootElement.GetProperty("title").GetString());
        Assert.Equal(notes, fetched.RootElement.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task UpdateTodo_LeavesOtherTodosAndContactsUnchanged_AC030()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var contactA = await NewContactAsync(client, "ac030a");
        var contactB = await NewContactAsync(client, "ac030b");
        var target = await CreateTodoAsync(client, "Target", contactA, "t", "2026-10-05");
        var otherLinked = await CreateTodoAsync(client, "Other linked", contactA, "o", "2026-11-05");
        var otherUnlinked = await CreateTodoAsync(client, "Other unlinked");
        var otherLinkedToB = await CreateTodoAsync(client, "Other linked to B", contactB, "b", "2026-12-05");
        var todoUrls = new[]
        {
            $"/api/todos/{otherLinked}", $"/api/todos/{otherUnlinked}", $"/api/todos/{otherLinkedToB}",
        };
        var contactUrls = new[] { $"/api/contacts/{contactA}", $"/api/contacts/{contactB}" };
        var urls = todoUrls.Concat(contactUrls).ToArray();
        var snapshot = new List<string>();
        foreach (var url in urls)
        {
            snapshot.Add(await GetBodyAsync(client, url));
        }

        factory.Time.Advance(TimeSpan.FromHours(3));
        var response = await PutAsync(client, target, $$"""{"title":"Target changed","contactId":"{{contactB}}"}""");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var fresh = factory.CreateClient();
        for (var i = 0; i < urls.Length; i++)
        {
            Assert.Equal(snapshot[i], await GetBodyAsync(fresh, urls[i]));
        }
    }

    private async Task<long> TodoCountAsync() =>
        await TodoStoreTests.ScalarAsync(factory.ConnectionString, "SELECT COUNT(*) FROM Todos");

    [Fact]
    public async Task UpdateTodo_UnknownGuid_Returns404AndCreatesNothing_AC026()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var unknown = Guid.CreateVersion7();
        var before = await TodoCountAsync();

        var response = await PutAsync(client, unknown, """{"title":"Ghost"}""");

        using var problem = await ProblemAssert.IsProblemAsync(response, 404);
        var get = await client.GetAsync($"/api/todos/{unknown}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(before, await TodoCountAsync());
    }

    // Guard: a non-GUID id never matches the {id:guid} route, so this passes before T-07.
    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("123")]
    public async Task UpdateTodo_NonGuidId_Returns404_AC026(string id)
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var before = await TodoCountAsync();

        var response = await client.PutAsync($"/api/todos/{id}", Json("""{"title":"Ghost"}"""), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await TodoCountAsync());
    }

    [Fact]
    public async Task UpdateTodo_UnknownContactToUnknownTodo_Returns404_AC028()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var before = await TodoCountAsync();

        var response = await PutAsync(client, Guid.CreateVersion7(),
            $$"""{"title":"Ghost","contactId":"{{Guid.CreateVersion7()}}"}""");

        using var problem = await ProblemAssert.IsProblemAsync(response, 404);
        Assert.False(problem.RootElement.TryGetProperty("errors", out _), "To-do existence is checked before contact existence");
        Assert.Equal(before, await TodoCountAsync());
    }

    [Fact]
    public async Task UpdateTodo_UnknownContactId_Returns400AndLeavesTodoUnchanged_AC029()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var original = await NewContactAsync(client, "ac029");
        var id = await CreateTodoAsync(client, "Keep me", original, "Keep notes", "2026-10-05");
        var before = await GetBodyAsync(client, $"/api/todos/{id}");
        var rowBefore = await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, id);
        factory.Time.Advance(TimeSpan.FromHours(1));

        var response = await PutAsync(client, id,
            $$"""{"title":"Changed","notes":"Changed notes","dueDate":"2027-01-01","contactId":"{{Guid.CreateVersion7()}}"}""");

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "contactId");
        Assert.Equal(
            ["Must refer to an existing contact."],
            problem.RootElement.GetProperty("errors").GetProperty("contactId").EnumerateArray().Select(e => e.GetString()).ToArray());
        using var fresh = factory.CreateClient();
        Assert.Equal(before, await GetBodyAsync(fresh, $"/api/todos/{id}"));
        var rowAfter = await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, id);
        Assert.NotNull(rowBefore);
        Assert.NotNull(rowAfter);
        Assert.Equal(rowBefore, rowAfter);
        Assert.Equal(TodoStoreTests.Upper(original), rowAfter["ContactId"]);
    }
}
