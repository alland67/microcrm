using System.Net.Http.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

namespace MicroCrm.Api.Tests.Integration;

// Own fixture instance: these tests break the database, so they must not share it with other classes.
public sealed class ErrorHandlingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task BreakDatabaseAsync()
    {
        // Force the host to start and migrations to run before the table is dropped.
        _ = factory.Server;
        await factory.ExecuteSqlAsync("DROP TABLE IF EXISTS Contacts");
    }

    private static async Task AssertSafe500Async(HttpResponseMessage response)
    {
        // AC-038 / AC-037 (500): problem+json with type/title/status, and no internals in the body.
        using var problem = await ProblemAssert.IsProblemAsync(response, 500);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("no such table", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqliteException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetContact_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC038()
    {
        await BreakDatabaseAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/contacts/{Guid.CreateVersion7()}", Ct);

        await AssertSafe500Async(response);
    }

    [Fact]
    public async Task ListContacts_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC038()
    {
        await BreakDatabaseAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/contacts", Ct);

        await AssertSafe500Async(response);
    }

    [Fact]
    public async Task CreateContact_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC038()
    {
        await BreakDatabaseAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/contacts", new { firstName = "Ada" }, Ct);

        await AssertSafe500Async(response);
    }

    [Fact]
    public async Task DeleteContact_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC038()
    {
        await BreakDatabaseAsync();
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/api/contacts/{Guid.CreateVersion7()}", Ct);

        await AssertSafe500Async(response);
    }

    [Fact]
    public async Task UpdateContact_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC038()
    {
        await BreakDatabaseAsync();
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/contacts/{Guid.CreateVersion7()}", new { firstName = "Ada" }, Ct);

        await AssertSafe500Async(response);
    }
}

// Separate fixture from ErrorHandlingTests: the trigger installed here must not leak into the tests that drop the table.
public sealed class NonUniqueConstraintFailureTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateContact_WhenNonUniqueConstraintFails_Returns500Problem_AC038()
    {
        // Force the host to start and migrations to run before the trigger is installed.
        _ = factory.Server;
        await factory.ExecuteSqlAsync(
            "CREATE TRIGGER FailContactInsert BEFORE INSERT ON Contacts BEGIN SELECT RAISE(ABORT, 'x'); END;");
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/contacts",
            new { firstName = "Ada", email = "ada.trigger@example.com" },
            Ct);

        // A constraint failure that is not a UNIQUE violation must stay a 500, never a 409 "email already exists".
        Assert.NotEqual(System.Net.HttpStatusCode.Conflict, response.StatusCode);
        using var problem = await ProblemAssert.IsProblemAsync(response, 500);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("SqliteException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
    }
}

// Own fixture: the BEFORE UPDATE trigger must not leak into other classes.
public sealed class NonUniqueUpdateConstraintFailureTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UpdateContact_WhenNonUniqueConstraintFails_Returns500AndLeavesContactUnchanged_AC038()
    {
        using var client = factory.CreateClient(); // starts the host and applies migrations
        var created = await client.PostAsJsonAsync(
            "/api/contacts",
            new { firstName = "Before", email = "before.trigger@example.com" },
            Ct);
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        using var createdDoc = System.Text.Json.JsonDocument.Parse(await created.Content.ReadAsStringAsync(Ct));
        var id = createdDoc.RootElement.GetProperty("id").GetGuid();
        await factory.ExecuteSqlAsync(
            "CREATE TRIGGER FailContactUpdate BEFORE UPDATE ON Contacts BEGIN SELECT RAISE(ABORT, 'x'); END;");

        var response = await client.PutAsJsonAsync(
            $"/api/contacts/{id}",
            new { firstName = "After", email = "after.trigger@example.com" },
            Ct);

        // A constraint failure that is not a UNIQUE violation must stay a 500, never a 409.
        Assert.NotEqual(System.Net.HttpStatusCode.Conflict, response.StatusCode);
        using var problem = await ProblemAssert.IsProblemAsync(response, 500);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("SqliteException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);

        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT FirstName, Email FROM Contacts WHERE lower(Id) = $id";
        command.Parameters.AddWithValue("$id", id.ToString().ToLowerInvariant());
        await using var rows = await command.ExecuteReaderAsync(Ct);
        Assert.True(await rows.ReadAsync(Ct));
        Assert.Equal("Before", rows.GetString(0));
        Assert.Equal("before.trigger@example.com", rows.GetString(1));
    }
}

// Own fixture: the BEFORE DELETE trigger must not leak into other classes.
public sealed class DeleteRejectedByDatabaseTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DeleteContact_WhenDatabaseRejects_Returns500AndContactStillExists_AC038()
    {
        using var client = factory.CreateClient(); // starts the host and applies migrations
        var created = await client.PostAsJsonAsync(
            "/api/contacts",
            new { firstName = "Stays", email = "stays.delete.trigger@example.com" },
            Ct);
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        using var createdDoc = System.Text.Json.JsonDocument.Parse(await created.Content.ReadAsStringAsync(Ct));
        var id = createdDoc.RootElement.GetProperty("id").GetGuid();
        await factory.ExecuteSqlAsync(
            "CREATE TRIGGER FailContactDelete BEFORE DELETE ON Contacts BEGIN SELECT RAISE(ABORT, 'x'); END;");

        var response = await client.DeleteAsync($"/api/contacts/{id}", Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 500);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("SqliteException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);

        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT FirstName FROM Contacts WHERE lower(Id) = $id";
        command.Parameters.AddWithValue("$id", id.ToString().ToLowerInvariant());
        Assert.Equal("Stays", (string?)await command.ExecuteScalarAsync(Ct));
    }
}
