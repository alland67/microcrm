using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

using Microsoft.Data.Sqlite;

namespace MicroCrm.Api.Tests.Integration.Contacts;

public sealed class ListContactsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task<JsonDocument> CreateAsync(HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync("/api/contacts", request, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
    }

    private static string[] PropertyNames(JsonElement element)
        => element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public async Task ListContacts_NoQuery_ReturnsFirst20WithEnvelope_AC020()
    {
        using var client = factory.CreateClient();
        var seeded = new HashSet<Guid>();
        string[]? createdShape = null;
        for (var i = 0; i < 25; i++)
        {
            using var created = await CreateAsync(
                client,
                new { firstName = $"First{i:D2}", lastName = $"Last{i:D2}", email = $"list{i:D2}.ac020@example.com" });
            seeded.Add(created.RootElement.GetProperty("id").GetGuid());
            createdShape ??= PropertyNames(created.RootElement);
        }

        var response = await client.GetAsync("/api/contacts", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var root = doc.RootElement;
        Assert.Equal(
            new[] { "items", "page", "pageSize", "totalCount" },
            PropertyNames(root));
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(20, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(25, root.GetProperty("totalCount").GetInt32());
        var items = root.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(20, items.Length);
        foreach (var item in items)
        {
            Assert.Equal(createdShape, PropertyNames(item));
        }

        // Which 20 and in what order is the sort task's concern (T-12); only membership and uniqueness here.
        var ids = items.Select(i => i.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Contains(id, seeded));
    }

    [Fact]
    public async Task ListContacts_NoContacts_ReturnsEmptyItemsAndZeroTotal_AC021()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/contacts", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Equal(0, items.GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task ListContacts_FromNewClient_IncludesPersistedContact_AC039()
    {
        Guid id;
        JsonDocument created;
        using (var creator = factory.CreateClient())
        {
            created = await CreateAsync(
                creator,
                new
                {
                    firstName = "Persisted",
                    lastName = "Listed",
                    email = "persisted.list.ac039@example.com",
                    phone = "123",
                    company = "Acme",
                    notes = "n",
                });
            id = created.RootElement.GetProperty("id").GetGuid();
        }

        using (created)
        {
            // The row must be in the database itself, seen through an independent connection.
            await using (var connection = new SqliteConnection(factory.ConnectionString))
            {
                await connection.OpenAsync(Ct);
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM Contacts WHERE lower(Id) = $id";
                command.Parameters.AddWithValue("$id", id.ToString().ToLowerInvariant());
                Assert.Equal(1L, (long)(await command.ExecuteScalarAsync(Ct))!);
            }

            using var reader = factory.CreateClient();
            var response = await reader.GetAsync("/api/contacts", Ct);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
            var match = Assert.Single(
                doc.RootElement.GetProperty("items").EnumerateArray(),
                i => i.GetProperty("id").GetGuid() == id);
            Assert.Equal(created.RootElement.GetRawText(), match.GetRawText());
        }
    }

    private sealed record Seed(string Email, string FirstName, string? LastName);

    // Creates contacts in the given order, advancing the clock between creates so that
    // GUID v7 ids ascend in insertion order (ids made in one millisecond are not ordered).
    private async Task SeedAsync(HttpClient client, params Seed[] seeds)
    {
        foreach (var seed in seeds)
        {
            factory.Time.Advance(TimeSpan.FromMilliseconds(5));
            object request = seed.LastName is null
                ? new { firstName = seed.FirstName, email = seed.Email }
                : new { firstName = seed.FirstName, lastName = seed.LastName, email = seed.Email };
            using var created = await CreateAsync(client, request);
        }
    }

    private static async Task<string[]> ListEmailsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/contacts", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("email").GetString()!)
            .ToArray();
    }

    private const string LowId = "00000000-0000-7000-8000-000000000000";
    private const string HighId = "FFFFFFFF-FFFF-7FFF-BFFF-FFFFFFFFFFFF";

    // Inserts two contacts that tie on last and first name, the one with the HIGHER id first,
    // so insertion order is the opposite of the expected id order. Ids are stored as uppercase
    // TEXT compared ordinally; timestamps use the format EF stores for DateTimeOffset.
    private async Task InsertTiedPairAsync(string lowEmail, string highEmail, string firstName, string? lastName)
    {
        var last = lastName is null ? "NULL" : $"'{lastName}'";
        await factory.ExecuteSqlAsync(
            "INSERT INTO Contacts (Id, FirstName, LastName, Email, CreatedAt, UpdatedAt) VALUES " +
            $"('{HighId}', '{firstName}', {last}, '{highEmail}', '2026-01-01 00:00:00+00:00', '2026-01-01 00:00:00+00:00')");
        await factory.ExecuteSqlAsync(
            "INSERT INTO Contacts (Id, FirstName, LastName, Email, CreatedAt, UpdatedAt) VALUES " +
            $"('{LowId}', '{firstName}', {last}, '{lowEmail}', '2026-01-01 00:00:00+00:00', '2026-01-01 00:00:00+00:00')");
    }

    [Fact]
    public async Task ListContacts_SortsByLastFirstIdIgnoringCase_AC022()
    {
        using var client = factory.CreateClient();

        // Tied pair inserted high id first; the rest seeded in reverse of the expected order.
        await InsertTiedPairAsync("dupont-lo.ac022@example.com", "dupont-hi.ac022@example.com", "Pat", "Dupont");
        await SeedAsync(
            client,
            new Seed("carter.ac022@example.com", "Eve", "carter"),
            new Seed("baker.ac022@example.com", "Dan", "Baker"),
            new Seed("adams-bob.ac022@example.com", "Bob", "Adams"),
            new Seed("adams-alice.ac022@example.com", "alice", "adams"));

        var actual = await ListEmailsAsync(client);

        var expected = new[]
            {
                "adams-alice.ac022@example.com", // adams/alice before Adams/Bob: same last name ignoring case, then first name ignoring case
                "adams-bob.ac022@example.com",
                "baker.ac022@example.com",
                "carter.ac022@example.com",
            }
            .Concat(new[] { "dupont-lo.ac022@example.com", "dupont-hi.ac022@example.com" }) // tie broken by id
            .ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ListContacts_ContactsWithoutLastName_SortLastByFirstNameThenId_AC023()
    {
        using var client = factory.CreateClient();

        // Tied pair inserted high id first; the rest seeded in reverse of the expected order.
        await InsertTiedPairAsync("dana-lo.ac023@example.com", "dana-hi.ac023@example.com", "Dana", null);
        await SeedAsync(
            client,
            new Seed("carl.ac023@example.com", "carl", null),
            new Seed("bob.ac023@example.com", "Bob", null),
            new Seed("alice.ac023@example.com", "alice", null),
            new Seed("zimmer.ac023@example.com", "Zoe", "Zimmer"),
            new Seed("adams.ac023@example.com", "Al", "adams"));

        var actual = await ListEmailsAsync(client);

        var expected = new[]
            {
                "adams.ac023@example.com",
                "zimmer.ac023@example.com",
                "alice.ac023@example.com",
                "bob.ac023@example.com",
                "carl.ac023@example.com",
            }
            .Concat(new[] { "dana-lo.ac023@example.com", "dana-hi.ac023@example.com" }) // tie broken by id
            .ToArray();
        Assert.Equal(expected, actual);
    }
}
