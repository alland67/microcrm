using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

using Microsoft.Data.Sqlite;

namespace MicroCrm.Api.Tests.Integration.Contacts;

public sealed class GetContactByIdTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("123")]
    public async Task GetContact_WithNonGuidId_Returns404Problem_AC019(string id)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/contacts/{id}", TestContext.Current.CancellationToken);

        // AC-019 (404) and AC-037 (problem+json with type/title/status)
        using var problem = await ProblemAssert.IsProblemAsync(response, 404);
    }

    [Fact]
    public async Task GetContact_Existing_Returns200WithCreatedBody_AC017()
    {
        using var client = factory.CreateClient();
        // Sub-millisecond ticks: a lossy SQLite round trip (e.g. truncation to ms) would change the timestamps.
        factory.Time.SetUtcNow(new DateTimeOffset(2031, 5, 6, 7, 8, 9, TimeSpan.Zero).AddTicks(1234567));
        var created = await client.PostAsJsonAsync(
            "/api/contacts",
            new
            {
                firstName = "Ada",
                lastName = "Lovelace",
                email = "ada.ac017@example.com",
                phone = "+44 20 7946 0000",
                company = "Analytical Engines Ltd",
                notes = "First programmer.",
            },
            Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdBody = await created.Content.ReadAsStringAsync(Ct);
        using var createdDoc = JsonDocument.Parse(createdBody);
        var id = createdDoc.RootElement.GetProperty("id").GetGuid();

        var response = await client.GetAsync($"/api/contacts/{id}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Equal(createdBody, body);
    }

    [Fact]
    public async Task GetContact_UnknownGuid_Returns404Problem_AC018()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/contacts/{Guid.CreateVersion7()}", Ct);

        // AC-018 / AC-037. Guard: passes at RED through the status-code pages while no GET route exists.
        using var problem = await ProblemAssert.IsProblemAsync(response, 404);
    }

    [Fact]
    public async Task GetContact_FromNewClientAndConnection_ReturnsPersistedContact_AC039()
    {
        string createdBody;
        Guid id;
        using (var creator = factory.CreateClient())
        {
            var created = await creator.PostAsJsonAsync(
                "/api/contacts",
                new { firstName = "Persisted", lastName = "Contact", email = "persisted.ac039@example.com" },
                Ct);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            createdBody = await created.Content.ReadAsStringAsync(Ct);
            using var createdDoc = JsonDocument.Parse(createdBody);
            id = createdDoc.RootElement.GetProperty("id").GetGuid();
        }

        // The row must be in the database itself, seen through an independent connection.
        await using (var connection = new SqliteConnection(factory.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Contacts WHERE lower(Id) = $id";
            command.Parameters.AddWithValue("$id", id.ToString().ToLowerInvariant());
            var count = (long)(await command.ExecuteScalarAsync(Ct))!;
            Assert.Equal(1L, count);
        }

        using var reader = factory.CreateClient();
        var response = await reader.GetAsync($"/api/contacts/{id}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(createdBody, await response.Content.ReadAsStringAsync(Ct));
    }
}
