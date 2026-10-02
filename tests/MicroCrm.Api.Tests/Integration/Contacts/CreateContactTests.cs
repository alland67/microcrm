using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

namespace MicroCrm.Api.Tests.Integration.Contacts;

public sealed class CreateContactTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

    [Fact]
    public async Task CreateContact_WithAllFields_Returns201WithLocationAndBody_AC001()
    {
        using var client = factory.CreateClient();
        var request = new
        {
            firstName = "Ada",
            lastName = "Lovelace",
            email = "ada.ac001@example.com",
            phone = "+44 20 7946 0000",
            company = "Analytical Engines Ltd",
            notes = "First programmer.",
        };

        var response = await client.PostAsJsonAsync("/api/contacts", request, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = await ReadJsonAsync(response);
        var root = doc.RootElement;
        var id = root.GetProperty("id").GetGuid();
        Assert.NotNull(response.Headers.Location);
        var locationPath = response.Headers.Location.IsAbsoluteUri
            ? response.Headers.Location.AbsolutePath
            : response.Headers.Location.OriginalString;
        Assert.Equal($"/api/contacts/{id}", locationPath, ignoreCase: true);
        Assert.Equal("Ada", root.GetProperty("firstName").GetString());
        Assert.Equal("Lovelace", root.GetProperty("lastName").GetString());
        Assert.Equal("ada.ac001@example.com", root.GetProperty("email").GetString());
        Assert.Equal("+44 20 7946 0000", root.GetProperty("phone").GetString());
        Assert.Equal("Analytical Engines Ltd", root.GetProperty("company").GetString());
        Assert.Equal("First programmer.", root.GetProperty("notes").GetString());
        Assert.Equal(factory.Time.GetUtcNow(), root.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(factory.Time.GetUtcNow(), root.GetProperty("updatedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task CreateContact_WithOnlyFirstName_ReturnsNullOptionalFields_AC002()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/contacts", new { firstName = "Grace" }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = await ReadJsonAsync(response);
        var root = doc.RootElement;
        Assert.Equal("Grace", root.GetProperty("firstName").GetString());
        foreach (var name in new[] { "lastName", "email", "phone", "company", "notes" })
        {
            Assert.True(root.TryGetProperty(name, out var value), $"'{name}' is missing from the JSON");
            Assert.Equal(JsonValueKind.Null, value.ValueKind);
        }
    }

    [Fact]
    public async Task CreateContact_IgnoresClientId_AssignsNewGuidV7_AC003()
    {
        using var client = factory.CreateClient();
        var clientId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var first = await client.PostAsJsonAsync("/api/contacts", new { id = clientId, firstName = "Linus" }, Ct);
        var second = await client.PostAsJsonAsync("/api/contacts", new { id = clientId, firstName = "Linus" }, Ct);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        using var firstDoc = await ReadJsonAsync(first);
        using var secondDoc = await ReadJsonAsync(second);
        var firstId = firstDoc.RootElement.GetProperty("id").GetGuid();
        var secondId = secondDoc.RootElement.GetProperty("id").GetGuid();
        Assert.NotEqual(clientId, firstId);
        Assert.NotEqual(Guid.Empty, firstId);
        Assert.Equal(7, firstId.Version);
        Assert.Equal(7, secondId.Version);
        Assert.NotEqual(firstId, secondId);
    }

    [Fact]
    public async Task CreateContact_SetsTimestampsFromClock_IgnoresClientValues_AC004()
    {
        using var client = factory.CreateClient();
        // Sub-millisecond ticks make the exact equality below meaningful. The 201 body is built from the
        // in-memory entity; the lossy-round-trip check through SQLite lives in the AC-017 test.
        var now = new DateTimeOffset(2030, 6, 7, 8, 9, 10, TimeSpan.Zero).AddTicks(1234567);
        factory.Time.SetUtcNow(now);

        var response = await client.PostAsJsonAsync(
            "/api/contacts",
            new
            {
                firstName = "Clock",
                createdAt = "1999-01-01T00:00:00Z",
                updatedAt = "1999-12-31T00:00:00Z",
            },
            Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = await ReadJsonAsync(response);
        var createdAt = doc.RootElement.GetProperty("createdAt").GetDateTimeOffset();
        var updatedAt = doc.RootElement.GetProperty("updatedAt").GetDateTimeOffset();
        Assert.Equal(now, createdAt);
        Assert.Equal(createdAt, updatedAt);
    }

    [Fact]
    public async Task CreateContact_WithUppercaseEmail_PreservesCasing_AC012()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/contacts",
            new { firstName = "Ada", email = "  Ada.Lovelace@Example.com " },
            Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = await ReadJsonAsync(response);
        Assert.Equal("Ada.Lovelace@Example.com", doc.RootElement.GetProperty("email").GetString());
    }

    [Fact]
    public async Task CreateContact_Json_IsCamelCaseWithNullsAndUtcOffsets_NFR002()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/contacts", new { firstName = "Raw" }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = await ReadJsonAsync(response);
        var root = doc.RootElement;
        var names = root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
        var expected = new[]
        {
            "company", "createdAt", "email", "firstName", "id", "lastName", "notes", "phone", "updatedAt",
        };
        Assert.Equal(expected, names);
        foreach (var name in new[] { "lastName", "email", "phone", "company", "notes" })
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
    public async Task CreateContact_WithSurroundingWhitespace_StoresTrimmed_AC005()
    {
        using var client = factory.CreateClient();
        var request = new
        {
            firstName = "  Ada\t",
            lastName = "\n Lovelace  ",
            email = "  ada.ac005@example.com ",
            phone = " +44 20 7946 0000  ",
            company = "   Analytical Engines Ltd ",
            notes = "  First programmer.\n",
        };

        var created = await client.PostAsJsonAsync("/api/contacts", request, Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdDoc = await ReadJsonAsync(created);
        var id = createdDoc.RootElement.GetProperty("id").GetGuid();
        var fetched = await client.GetAsync($"/api/contacts/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        using var doc = await ReadJsonAsync(fetched);
        var root = doc.RootElement;
        Assert.Equal("Ada", root.GetProperty("firstName").GetString());
        Assert.Equal("Lovelace", root.GetProperty("lastName").GetString());
        Assert.Equal("ada.ac005@example.com", root.GetProperty("email").GetString());
        Assert.Equal("+44 20 7946 0000", root.GetProperty("phone").GetString());
        Assert.Equal("Analytical Engines Ltd", root.GetProperty("company").GetString());
        Assert.Equal("First programmer.", root.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task CreateContact_WithBlankOptionalFields_ReturnsNulls_AC006()
    {
        using var client = factory.CreateClient();
        var request = new
        {
            firstName = "Blank",
            lastName = "",
            email = "   ",
            phone = "",
            company = "   ",
            notes = " \t ",
        };

        var created = await client.PostAsJsonAsync("/api/contacts", request, Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdDoc = await ReadJsonAsync(created);
        var id = createdDoc.RootElement.GetProperty("id").GetGuid();
        AssertOptionalFieldsNull(createdDoc.RootElement);

        var fetched = await client.GetAsync($"/api/contacts/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        using var doc = await ReadJsonAsync(fetched);
        AssertOptionalFieldsNull(doc.RootElement);
    }

    // Guard: no limits exist yet, so this passes at RED. It catches an off-by-one or measuring before trimming.
    [Fact]
    public async Task CreateContact_FieldsAtMax_Returns201_AC009()
    {
        using var client = factory.CreateClient();
        var email = new string('a', 254 - "@example.com".Length) + "@example.com";
        var request = new
        {
            firstName = "  " + new string('f', 100) + " ",
            lastName = new string('l', 100),
            email,
            phone = " " + new string('1', 50) + "\t",
            company = new string('c', 200),
            notes = "\n" + new string('n', 4000) + "  ",
        };

        var response = await client.PostAsJsonAsync("/api/contacts", request, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = await ReadJsonAsync(response);
        var root = doc.RootElement;
        Assert.Equal(new string('f', 100), root.GetProperty("firstName").GetString());
        Assert.Equal(new string('l', 100), root.GetProperty("lastName").GetString());
        Assert.Equal(email, root.GetProperty("email").GetString());
        Assert.Equal(new string('1', 50), root.GetProperty("phone").GetString());
        Assert.Equal(new string('c', 200), root.GetProperty("company").GetString());
        Assert.Equal(new string('n', 4000), root.GetProperty("notes").GetString());
    }

    private static void AssertOptionalFieldsNull(JsonElement root)
    {
        foreach (var name in new[] { "lastName", "email", "phone", "company", "notes" })
        {
            Assert.True(root.TryGetProperty(name, out var value), $"'{name}' is missing from the JSON");
            Assert.Equal(JsonValueKind.Null, value.ValueKind);
        }
    }
}
