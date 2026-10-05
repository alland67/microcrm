using System.Net.Http.Json;
using System.Text;

using MicroCrm.Api.Tests.Integration.Infrastructure;

using Microsoft.Data.Sqlite;

namespace MicroCrm.Api.Tests.Integration.Contacts;

public sealed class CreateContactValidationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The first argument is the raw JSON fragment for the firstName member ("" omits the member entirely).
    [Theory]
    [InlineData("missing", "")]
    [InlineData("null", "\"firstName\":null,")]
    [InlineData("empty", "\"firstName\":\"\",")]
    [InlineData("whitespace", "\"firstName\":\"  \",")]
    public async Task CreateContact_WithoutFirstName_Returns400WithFirstNameError_AC007(string caseName, string firstNameMember)
    {
        var marker = $"ac007-{caseName}@example.com";
        using var client = factory.CreateClient();
        using var content = new StringContent($"{{{firstNameMember}\"email\":\"{marker}\"}}", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/contacts", content, Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "firstName");
        Assert.Equal(0, await CountContactsWithEmailAsync(marker));
    }

    [Theory]
    [InlineData("firstName", 100)]
    [InlineData("lastName", 100)]
    [InlineData("email", 254)]
    [InlineData("phone", 50)]
    [InlineData("company", 200)]
    [InlineData("notes", 4000)]
    public async Task CreateContact_FieldOverMax_Returns400WithFieldError_AC008(string field, int max)
    {
        // Email is well-formed (one '@', no whitespace) and unique to this case, so only its length is wrong.
        var value = field == "email"
            ? new string('a', max + 1 - "@example.com".Length) + "@example.com"
            : new string('x', max + 1);
        Assert.Equal(max + 1, value.Length);
        var body = new Dictionary<string, string> { ["firstName"] = "Ada", [field] = value };
        using var client = factory.CreateClient(); // starts the host, which creates the schema
        var before = await CountContactsAsync();

        var response = await client.PostAsJsonAsync("/api/contacts", body, Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, field);
        Assert.Equal(before, await CountContactsAsync());
    }

    [Fact]
    public async Task CreateContact_SeveralFieldsOverMax_Returns400WithErrorForEach_AC008()
    {
        var body = new Dictionary<string, string>
        {
            ["firstName"] = "Ada",
            ["lastName"] = new string('x', 101),
            ["phone"] = new string('x', 51),
            ["notes"] = new string('x', 4001),
        };
        using var client = factory.CreateClient();
        var before = await CountContactsAsync();

        var response = await client.PostAsJsonAsync("/api/contacts", body, Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "lastName", "phone", "notes");
        Assert.Equal(before, await CountContactsAsync());
    }

    [Fact]
    public async Task CreateContact_WithInvalidEmail_Returns400WithEmailError_AC010()
    {
        var body = new Dictionary<string, string> { ["firstName"] = "Ada", ["email"] = "not-an-email" };
        using var client = factory.CreateClient();
        var before = await CountContactsAsync();

        var response = await client.PostAsJsonAsync("/api/contacts", body, Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "email");
        Assert.Equal(before, await CountContactsAsync());
    }

    [Fact]
    public async Task CreateContact_WithMultipleInvalidFields_ReturnsAllCamelCaseKeys_AC014()
    {
        var body = new Dictionary<string, string>
        {
            ["email"] = "not-an-email",
            ["phone"] = new string('1', 51),
        };
        using var client = factory.CreateClient();
        var before = await CountContactsAsync();

        var response = await client.PostAsJsonAsync("/api/contacts", body, Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "firstName", "email", "phone");
        Assert.Equal(before, await CountContactsAsync());
    }

    [Theory]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"firstName\":123}")]
    public async Task CreateContact_WithMalformedBody_Returns400Problem_AC015(string rawBody)
    {
        using var client = factory.CreateClient(); // starts the host, which creates the schema
        var before = await CountContactsAsync();
        using var content = new StringContent(rawBody, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/contacts", content, Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400);
        Assert.Equal(before, await CountContactsAsync());
    }

    [Fact]
    public async Task CreateContact_ValidationMessages_FollowStyle_AC039()
    {
        var body = new Dictionary<string, string>
        {
            ["email"] = "not-an-email",
            ["phone"] = new string('1', 51),
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/contacts", body, Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "firstName", "email", "phone");
        ProblemAssert.AssertValidationMessageStyle(problem);
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["Required."], errors.GetProperty("firstName").EnumerateArray().Select(e => e.GetString()).ToArray());
        // Guards (already compliant at RED):
        Assert.Equal(["Must be a valid email address."], errors.GetProperty("email").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Equal(["Must be 50 characters or fewer."], errors.GetProperty("phone").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    private async Task<long> CountContactsAsync()
    {
        await using var connection = new SqliteConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Contacts";
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<long> CountContactsWithEmailAsync(string email)
    {
        await using var connection = new SqliteConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Contacts WHERE Email = $email";
        command.Parameters.AddWithValue("$email", email);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }
}
