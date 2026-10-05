using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

using Microsoft.Data.Sqlite;

namespace MicroCrm.Api.Tests.Integration.Contacts;

public sealed class UpdateContactValidationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private record Row(string FirstName, string? LastName, string? Email, string? Phone, string? Company, string? Notes, string UpdatedAt);

    private static async Task<Guid> SeedAsync(HttpClient client, string tag)
    {
        var response = await client.PostAsJsonAsync(
            "/api/contacts",
            new
            {
                firstName = "Seed",
                lastName = "Person",
                email = $"seed.{tag}@example.com",
                phone = "111",
                company = "SeedCo",
                notes = "seed notes",
            },
            Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> PutRawAsync(HttpClient client, Guid id, string json)
        => client.PutAsync($"/api/contacts/{id}", new StringContent(json, Encoding.UTF8, "application/json"), Ct);

    private async Task<Row> ReadRowAsync(Guid id)
    {
        await using var connection = new SqliteConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT FirstName, LastName, Email, Phone, Company, Notes, UpdatedAt FROM Contacts WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id.ToString().ToUpperInvariant());
        await using var reader = await command.ExecuteReaderAsync(Ct);
        Assert.True(await reader.ReadAsync(Ct), "seeded contact row not found");
        string? S(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
        return new Row(reader.GetString(0), S(1), S(2), S(3), S(4), S(5), reader.GetValue(6).ToString()!);
    }

    private async Task<long> CountAsync()
    {
        await using var connection = new SqliteConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Contacts";
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("whitespace")]
    public async Task UpdateContact_WithoutFirstName_Returns400WithFirstNameError_AC017(string variant)
    {
        using var client = factory.CreateClient();
        var id = await SeedAsync(client, "ac017-" + variant);
        var before = await ReadRowAsync(id);
        var body = variant switch
        {
            "missing" => """{"lastName":"New"}""",
            "null" => """{"firstName":null,"lastName":"New"}""",
            "empty" => """{"firstName":"","lastName":"New"}""",
            _ => """{"firstName":"   ","lastName":"New"}""",
        };

        var response = await PutRawAsync(client, id, body);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "firstName");
        Assert.Equal(before, await ReadRowAsync(id));
    }

    [Theory]
    [InlineData("firstName", 100)]
    [InlineData("lastName", 100)]
    [InlineData("email", 254)]
    [InlineData("phone", 50)]
    [InlineData("company", 200)]
    [InlineData("notes", 4000)]
    public async Task UpdateContact_FieldOverMax_Returns400WithFieldError_AC018(string field, int max)
    {
        using var client = factory.CreateClient();
        var id = await SeedAsync(client, "ac018-" + field);
        var before = await ReadRowAsync(id);
        var body = new Dictionary<string, string> { ["firstName"] = "Valid" };
        // Email must be over max but otherwise shaped like an email so only length is in question.
        body[field] = field == "email" ? new string('a', max + 1 - "@example.com".Length) + "@example.com" : new string('x', max + 1);

        var response = await client.PutAsJsonAsync($"/api/contacts/{id}", body, Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400);
        var errors = problem.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty(field, out _), $"errors is missing '{field}'");
        Assert.Equal(before, await ReadRowAsync(id));
    }

    [Fact]
    public async Task UpdateContact_WithInvalidEmail_Returns400WithEmailError_AC019()
    {
        using var client = factory.CreateClient();
        var id = await SeedAsync(client, "ac019");
        var before = await ReadRowAsync(id);

        var response = await client.PutAsJsonAsync($"/api/contacts/{id}", new { firstName = "Valid", email = "not-an-email" }, Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "email");
        Assert.Equal(before, await ReadRowAsync(id));
    }

    [Fact]
    public async Task UpdateContact_WithMultipleInvalidFields_ReturnsAllCamelCaseKeys_AC020()
    {
        using var client = factory.CreateClient();
        var id = await SeedAsync(client, "ac020");
        var before = await ReadRowAsync(id);

        var response = await client.PutAsJsonAsync(
            $"/api/contacts/{id}",
            new { firstName = " ", lastName = new string('l', 101), email = "bad", phone = new string('1', 51), company = new string('c', 201), notes = new string('n', 4001) },
            Ct);

        using var problem = await ProblemAssert.IsProblemAsync(
            response, 400, "firstName", "lastName", "email", "phone", "company", "notes");
        Assert.Equal(before, await ReadRowAsync(id));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"firstName":123}""")]
    public async Task UpdateContact_WithMalformedBody_Returns400Problem_AC021(string raw)
    {
        // Guard: binding failures are handled by the spec 001 pipeline before the handler runs.
        using var client = factory.CreateClient();
        var id = await SeedAsync(client, "ac021-" + Math.Abs(raw.GetHashCode()));
        var before = await ReadRowAsync(id);

        var response = await PutRawAsync(client, id, raw);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400);
        Assert.Equal(before, await ReadRowAsync(id));
    }

    [Fact]
    public async Task UpdateContact_InvalidBodyToUnknownId_Returns400_AC024()
    {
        using var client = factory.CreateClient();
        var before = await CountAsync();

        var response = await client.PutAsJsonAsync($"/api/contacts/{Guid.NewGuid()}", new { firstName = "", email = "bad" }, Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "firstName", "email");
        Assert.Equal(before, await CountAsync());
    }

    [Fact]
    public async Task UpdateContact_ValidationMessages_FollowStyle_AC039()
    {
        using var client = factory.CreateClient();
        var id = await SeedAsync(client, "ac039");

        var response = await client.PutAsJsonAsync(
            $"/api/contacts/{id}",
            new { email = "not-an-email", phone = new string('1', 51) },
            Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "firstName", "email", "phone");
        ProblemAssert.AssertValidationMessageStyle(problem);
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["Required."], errors.GetProperty("firstName").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public async Task UpdateContact_SameInvalidPayload_SameErrorsAsCreate_NFR004()
    {
        using var client = factory.CreateClient();
        var id = await SeedAsync(client, "nfr004");
        var payload = new { firstName = " ", email = "bad", phone = new string('1', 51), company = new string('c', 201) };

        var post = await client.PostAsJsonAsync("/api/contacts", payload, Ct);
        var put = await client.PutAsJsonAsync($"/api/contacts/{id}", payload, Ct);

        using var createProblem = await ProblemAssert.IsProblemAsync(post, 400);
        using var updateProblem = await ProblemAssert.IsProblemAsync(put, 400);
        Assert.Equal(
            createProblem.RootElement.GetProperty("errors").GetRawText(),
            updateProblem.RootElement.GetProperty("errors").GetRawText());
    }
}
