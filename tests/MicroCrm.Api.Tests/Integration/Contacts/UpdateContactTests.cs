using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

using Microsoft.Data.Sqlite;

namespace MicroCrm.Api.Tests.Integration.Contacts;

public sealed class UpdateContactTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] OptionalFields = ["lastName", "email", "phone", "company", "notes"];

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

    private static async Task<JsonDocument> CreateAsync(HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync("/api/contacts", request, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static Task<HttpResponseMessage> PutRawAsync(HttpClient client, Guid id, string json)
        => client.PutAsync(
            $"/api/contacts/{id}",
            new StringContent(json, Encoding.UTF8, "application/json"),
            Ct);

    // Seeds a contact with every field populated; unique per tag.
    private static async Task<JsonDocument> SeedFullAsync(HttpClient client, string tag)
        => await CreateAsync(
            client,
            new
            {
                firstName = "Seed",
                lastName = "Person",
                email = $"seed.{tag}@example.com",
                phone = "111",
                company = "SeedCo",
                notes = "seed notes",
            });

    private async Task<long> CountAsync()
    {
        await using var connection = new SqliteConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Contacts";
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    [Fact]
    public async Task UpdateContact_WithValidBody_Returns200WithUpdatedContact_AC001()
    {
        using var client = factory.CreateClient();
        using var seeded = await SeedFullAsync(client, "ac001");
        var id = seeded.RootElement.GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/contacts/{id}",
            new
            {
                firstName = "Grace",
                lastName = "Hopper",
                email = "grace.ac001@example.com",
                phone = "+1 555 0100",
                company = "US Navy",
                notes = "COBOL.",
            },
            Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await ReadJsonAsync(response);
        var root = doc.RootElement;
        Assert.Equal(id, root.GetProperty("id").GetGuid());
        Assert.Equal("Grace", root.GetProperty("firstName").GetString());
        Assert.Equal("Hopper", root.GetProperty("lastName").GetString());
        Assert.Equal("grace.ac001@example.com", root.GetProperty("email").GetString());
        Assert.Equal("+1 555 0100", root.GetProperty("phone").GetString());
        Assert.Equal("US Navy", root.GetProperty("company").GetString());
        Assert.Equal("COBOL.", root.GetProperty("notes").GetString());
        Assert.Equal(
            seeded.RootElement.GetProperty("createdAt").GetDateTimeOffset(),
            root.GetProperty("createdAt").GetDateTimeOffset());
        Assert.True(root.TryGetProperty("updatedAt", out _));
    }

    [Fact]
    public async Task UpdateContact_ThenRead_ReturnsPersistedValuesOnNewConnections_AC002()
    {
        using var client = factory.CreateClient();
        using var seeded = await SeedFullAsync(client, "ac002");
        var id = seeded.RootElement.GetProperty("id").GetGuid();

        var put = await client.PutAsJsonAsync(
            $"/api/contacts/{id}",
            new { firstName = "Persisted", lastName = "Zzpersist", email = "persisted.ac002@example.com", phone = "999" },
            Ct);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        // New HttpClient: GET by id.
        using var reader = factory.CreateClient();
        var get = await reader.GetAsync($"/api/contacts/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using var getDoc = await ReadJsonAsync(get);
        Assert.Equal("Persisted", getDoc.RootElement.GetProperty("firstName").GetString());
        Assert.Equal("persisted.ac002@example.com", getDoc.RootElement.GetProperty("email").GetString());
        Assert.Equal("999", getDoc.RootElement.GetProperty("phone").GetString());
        Assert.Equal(JsonValueKind.Null, getDoc.RootElement.GetProperty("company").ValueKind);

        // List via search.
        var list = await reader.GetAsync("/api/contacts?search=persisted.ac002", Ct);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listDoc = await ReadJsonAsync(list);
        var item = Assert.Single(listDoc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(id, item.GetProperty("id").GetGuid());
        Assert.Equal("Persisted", item.GetProperty("firstName").GetString());

        // Direct row read through an independent connection.
        await using var connection = new SqliteConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT FirstName, Email, Company FROM Contacts WHERE lower(Id) = $id";
        command.Parameters.AddWithValue("$id", id.ToString().ToLowerInvariant());
        await using var rows = await command.ExecuteReaderAsync(Ct);
        Assert.True(await rows.ReadAsync(Ct));
        Assert.Equal("Persisted", rows.GetString(0));
        Assert.Equal("persisted.ac002@example.com", rows.GetString(1));
        Assert.True(rows.IsDBNull(2));
    }

    public static TheoryData<string, string> OptionalFieldBlankCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var field in OptionalFields)
        {
            data.Add(field, "omitted");
            data.Add(field, "null");
            data.Add(field, "empty");
            data.Add(field, "whitespace");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(OptionalFieldBlankCases))]
    public async Task UpdateContact_OptionalFieldOmittedNullOrBlank_StoredAsNull_AC003(string field, string variant)
    {
        using var client = factory.CreateClient();
        using var seeded = await SeedFullAsync(client, $"ac003.{field}.{variant}");
        var id = seeded.RootElement.GetProperty("id").GetGuid();

        var body = new Dictionary<string, object?>
        {
            ["firstName"] = "Keep",
            ["lastName"] = "L",
            ["email"] = $"ac003.{field}.{variant}.new@example.com",
            ["phone"] = "P",
            ["company"] = "C",
            ["notes"] = "N",
        };
        switch (variant)
        {
            case "omitted": body.Remove(field); break;
            case "null": body[field] = null; break;
            case "empty": body[field] = ""; break;
            default: body[field] = "  \t "; break;
        }

        var response = await client.PutAsJsonAsync($"/api/contacts/{id}", body, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty(field).ValueKind);
        using var reader = factory.CreateClient();
        using var fetched = await ReadJsonAsync(await reader.GetAsync($"/api/contacts/{id}", Ct));
        Assert.Equal(JsonValueKind.Null, fetched.RootElement.GetProperty(field).ValueKind);
        foreach (var other in OptionalFields.Where(f => f != field))
        {
            Assert.Equal(JsonValueKind.String, fetched.RootElement.GetProperty(other).ValueKind);
        }
    }

    [Fact]
    public async Task UpdateContact_WithSurroundingWhitespace_StoresTrimmed_AC004()
    {
        using var client = factory.CreateClient();
        using var seeded = await SeedFullAsync(client, "ac004");
        var id = seeded.RootElement.GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/contacts/{id}",
            new
            {
                firstName = "  Ada\t",
                lastName = "\n Lovelace  ",
                email = "  ada.ac004@example.com ",
                phone = " +44 20 7946 0000  ",
                company = "   Analytical Engines Ltd ",
                notes = "  First programmer.\n",
            },
            Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var reader = factory.CreateClient();
        using var doc = await ReadJsonAsync(await reader.GetAsync($"/api/contacts/{id}", Ct));
        var root = doc.RootElement;
        Assert.Equal("Ada", root.GetProperty("firstName").GetString());
        Assert.Equal("Lovelace", root.GetProperty("lastName").GetString());
        Assert.Equal("ada.ac004@example.com", root.GetProperty("email").GetString());
        Assert.Equal("+44 20 7946 0000", root.GetProperty("phone").GetString());
        Assert.Equal("Analytical Engines Ltd", root.GetProperty("company").GetString());
        Assert.Equal("First programmer.", root.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task UpdateContact_IgnoresBodyIdAndTimestamps_KeepsIdAndCreatedAt_AC005()
    {
        using var client = factory.CreateClient();
        using var seeded = await SeedFullAsync(client, "ac005");
        var id = seeded.RootElement.GetProperty("id").GetGuid();
        var createdAt = seeded.RootElement.GetProperty("createdAt").GetDateTimeOffset();
        var otherId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        factory.Time.Advance(TimeSpan.FromMinutes(5));

        var response = await client.PutAsJsonAsync(
            $"/api/contacts/{id}",
            new
            {
                id = otherId,
                createdAt = "1999-01-01T00:00:00Z",
                updatedAt = "1999-12-31T00:00:00Z",
                firstName = "Same",
            },
            Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await ReadJsonAsync(response);
        Assert.Equal(id, doc.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(createdAt, doc.RootElement.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(factory.Time.GetUtcNow(), doc.RootElement.GetProperty("updatedAt").GetDateTimeOffset());

        using var reader = factory.CreateClient();
        using var fetched = await ReadJsonAsync(await reader.GetAsync($"/api/contacts/{id}", Ct));
        Assert.Equal(id, fetched.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(createdAt, fetched.RootElement.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(factory.Time.GetUtcNow(), fetched.RootElement.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/api/contacts/{otherId}", Ct)).StatusCode);
    }

    [Fact]
    public async Task UpdateContact_SetsUpdatedAtFromClock_EvenWhenValuesUnchanged_AC006()
    {
        using var client = factory.CreateClient();
        var request = new { firstName = "Clock", lastName = "Same", email = "clock.ac006@example.com" };
        using var seeded = await CreateAsync(client, request);
        var id = seeded.RootElement.GetProperty("id").GetGuid();
        var createdAt = seeded.RootElement.GetProperty("createdAt").GetDateTimeOffset();
        factory.Time.Advance(TimeSpan.FromHours(2));
        var later = factory.Time.GetUtcNow();

        var response = await client.PutAsJsonAsync($"/api/contacts/{id}", request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await ReadJsonAsync(response);
        Assert.Equal(later, doc.RootElement.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.Equal(createdAt, doc.RootElement.GetProperty("createdAt").GetDateTimeOffset());
        Assert.NotEqual(createdAt, later);

        using var reader = factory.CreateClient();
        using var fetched = await ReadJsonAsync(await reader.GetAsync($"/api/contacts/{id}", Ct));
        Assert.Equal(later, fetched.RootElement.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.Equal(createdAt, fetched.RootElement.GetProperty("createdAt").GetDateTimeOffset());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateContact_FieldsAtMax_Returns200_AC007(bool wrappedInWhitespace)
    {
        using var client = factory.CreateClient();
        using var seeded = await SeedFullAsync(client, $"ac007.{wrappedInWhitespace}");
        var id = seeded.RootElement.GetProperty("id").GetGuid();
        var local = $"max{wrappedInWhitespace}".ToLowerInvariant();
        var email = local + new string('a', 254 - "@example.com".Length - local.Length) + "@example.com";
        string Wrap(string s) => wrappedInWhitespace ? " \t" + s + "\n " : s;
        var firstName = new string('f', 100);
        var lastName = new string('l', 100);
        var phone = new string('1', 50);
        var company = new string('c', 200);
        var notes = new string('n', 4000);

        var response = await client.PutAsJsonAsync(
            $"/api/contacts/{id}",
            new
            {
                firstName = Wrap(firstName),
                lastName = Wrap(lastName),
                email = Wrap(email),
                phone = Wrap(phone),
                company = Wrap(company),
                notes = Wrap(notes),
            },
            Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await ReadJsonAsync(response);
        var root = doc.RootElement;
        Assert.Equal(firstName, root.GetProperty("firstName").GetString());
        Assert.Equal(lastName, root.GetProperty("lastName").GetString());
        Assert.Equal(email, root.GetProperty("email").GetString());
        Assert.Equal(phone, root.GetProperty("phone").GetString());
        Assert.Equal(company, root.GetProperty("company").GetString());
        Assert.Equal(notes, root.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task UpdateContact_WithUppercaseEmail_PreservesCasing_AC010()
    {
        using var client = factory.CreateClient();
        using var seeded = await SeedFullAsync(client, "ac010");
        var id = seeded.RootElement.GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/contacts/{id}",
            new { firstName = "Ada", email = "  Ada.AC010@Example.COM " },
            Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await ReadJsonAsync(response);
        Assert.Equal("Ada.AC010@Example.COM", doc.RootElement.GetProperty("email").GetString());
        using var reader = factory.CreateClient();
        using var fetched = await ReadJsonAsync(await reader.GetAsync($"/api/contacts/{id}", Ct));
        Assert.Equal("Ada.AC010@Example.COM", fetched.RootElement.GetProperty("email").GetString());
    }

    [Fact]
    public async Task UpdateContact_Json_IsCamelCaseWithNullsAndUtcOffsets_NFR002()
    {
        using var client = factory.CreateClient();
        using var seeded = await SeedFullAsync(client, "nfr002");
        var id = seeded.RootElement.GetProperty("id").GetGuid();

        var response = await PutRawAsync(client, id, """{"firstName":"Raw"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await ReadJsonAsync(response);
        var root = doc.RootElement;
        var names = root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(
            new[] { "company", "createdAt", "email", "firstName", "id", "lastName", "notes", "phone", "updatedAt" },
            names);
        foreach (var name in OptionalFields)
        {
            Assert.Equal(JsonValueKind.Null, root.GetProperty(name).ValueKind);
        }

        foreach (var name in new[] { "createdAt", "updatedAt" })
        {
            var text = root.GetProperty(name).GetString();
            Assert.NotNull(text);
            Assert.True(
                text.EndsWith('Z') || text.EndsWith("+00:00", StringComparison.Ordinal),
                $"'{name}' must carry a UTC offset, got '{text}'");
        }
    }

    [Fact]
    public async Task UpdateContact_UnknownGuid_Returns404ProblemAndCreatesNothing_AC022()
    {
        using var client = factory.CreateClient();
        using var seeded = await SeedFullAsync(client, "ac022");
        var before = await CountAsync();
        var unknown = Guid.CreateVersion7();

        var response = await client.PutAsJsonAsync($"/api/contacts/{unknown}", new { firstName = "Ghost" }, Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 404);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/contacts/{unknown}", Ct)).StatusCode);
        Assert.Equal(before, await CountAsync());
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("123")]
    public async Task UpdateContact_NonGuidId_Returns404Problem_AC023(string id)
    {
        using var client = factory.CreateClient();
        using var seeded = await SeedFullAsync(client, $"ac023.{id}");
        var before = await CountAsync();

        var response = await client.PutAsJsonAsync($"/api/contacts/{id}", new { firstName = "Ghost" }, Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 404);
        Assert.Equal(before, await CountAsync());
    }

    [Fact]
    public async Task UpdateContact_ConflictingEmailToUnknownId_Returns404_AC025()
    {
        using var client = factory.CreateClient();
        using var other = await SeedFullAsync(client, "ac025");
        var before = await CountAsync();
        var unknown = Guid.CreateVersion7();

        var response = await client.PutAsJsonAsync(
            $"/api/contacts/{unknown}",
            new { firstName = "Ghost", email = "SEED.AC025@example.com" },
            Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 404);
        Assert.Equal(before, await CountAsync());
    }
}
