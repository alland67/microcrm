using System.Net.Http.Json;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

namespace MicroCrm.Api.Tests.Integration.Todos;

// Own fixture instance: these tests drop the Todos table, so they must not share the database with other classes.
public sealed class TodoErrorHandlingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task BreakDatabaseAsync()
    {
        // Force the host to start and migrations to run before the table is dropped.
        _ = factory.Server;
        await factory.ExecuteSqlAsync("DROP TABLE IF EXISTS Todos");
    }

    private static async Task AssertSafe500Async(HttpResponseMessage response)
    {
        // AC-073: problem+json with type/title/status, and no internals in the body.
        using var problem = await ProblemAssert.IsProblemAsync(response, 500);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("no such table", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqliteException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateTodo_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC073()
    {
        await BreakDatabaseAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/todos", new { title = "Doomed" }, Ct);

        await AssertSafe500Async(response);
    }

    [Fact]
    public async Task GetTodo_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC073()
    {
        await BreakDatabaseAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/todos/{Guid.CreateVersion7()}", Ct);

        await AssertSafe500Async(response);
    }

    // Guard: passes before T-07 (the unhandled failure already surfaces as a safe 500).
    [Fact]
    public async Task UpdateTodo_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC073()
    {
        await BreakDatabaseAsync();
        using var client = factory.CreateClient();

        var response = await client.PutAsync(
            $"/api/todos/{Guid.CreateVersion7()}",
            new StringContent("""{"title":"Doomed"}""", System.Text.Encoding.UTF8, "application/json"),
            Ct);

        await AssertSafe500Async(response);
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("reopen")]
    public async Task CompleteOrReopenTodo_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC073(string action)
    {
        await BreakDatabaseAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/api/todos/{Guid.CreateVersion7()}/{action}", null, Ct);

        await AssertSafe500Async(response);
    }
}

// Own fixture: installs a BEFORE INSERT trigger on the shared in-memory database and drops it in finally.
public sealed class CreateTodoNonForeignKeyFailureTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> SnapshotAsync()
    {
        await using var connection = await TodoStoreTests.OpenAsync(factory.ConnectionString);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT 'T|' || Id || '|' || Title || '|' || ifnull(ContactId, '-') || '|' || UpdatedAt FROM Todos " +
            "UNION ALL SELECT 'C|' || Id FROM Contacts ORDER BY 1";
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var lines = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            lines.Add(reader.GetString(0));
        }

        return string.Join("\n", lines);
    }

    // AC-073 + ADR-0008: a constraint failure that is not a foreign key (trigger abort, extended code 1811)
    // must stay a safe 500; only extended code 787 maps to the contactId 400.
    [Fact]
    public async Task CreateTodo_WhenNonForeignKeyConstraintFails_Returns500NotContactError_AC073()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var contactId = await TodoStoreTests.CreateContactAsync(client, "Trigger", $"trigger.{Guid.NewGuid():N}@example.com");
        var existing = await client.PostAsJsonAsync("/api/todos", new { title = "Pre-existing", contactId }, Ct);
        Assert.Equal(System.Net.HttpStatusCode.Created, existing.StatusCode);
        var before = await SnapshotAsync();

        await factory.ExecuteSqlAsync(
            "CREATE TRIGGER trg_fail_insert_todo BEFORE INSERT ON Todos BEGIN SELECT RAISE(ABORT, 'x'); END");
        HttpResponseMessage response;
        string body;
        try
        {
            response = await client.PostAsJsonAsync("/api/todos", new { title = "Doomed", contactId }, Ct);
            body = await response.Content.ReadAsStringAsync(Ct);
        }
        finally
        {
            await factory.ExecuteSqlAsync("DROP TRIGGER IF EXISTS trg_fail_insert_todo");
        }

        Assert.Equal(500, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(500, problem.RootElement.GetProperty("status").GetInt32());
        Assert.False(problem.RootElement.TryGetProperty("errors", out _), "A non-FK failure must not become a validation error");
        Assert.DoesNotContain("existing contact", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contactId", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Sqlite", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
        Assert.DoesNotContain("RAISE", body, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(before, await SnapshotAsync());
    }
}

// Own fixture: installs a BEFORE UPDATE trigger on the shared in-memory database and drops it in finally.
public sealed class UpdateTodoNonForeignKeyFailureTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> SnapshotAsync()
    {
        await using var connection = await TodoStoreTests.OpenAsync(factory.ConnectionString);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT 'T|' || Id || '|' || Title || '|' || ifnull(ContactId, '-') || '|' || UpdatedAt FROM Todos " +
            "UNION ALL SELECT 'C|' || Id FROM Contacts ORDER BY 1";
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var lines = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            lines.Add(reader.GetString(0));
        }

        return string.Join("\n", lines);
    }

    // Guard (AC-073 + ADR-0008): a non-FK constraint failure (trigger abort, extended code 1811) must stay a
    // safe 500; only extended code 787 maps to the contactId 400. Passes before T-07 and pins the catch's scope.
    [Fact]
    public async Task UpdateTodo_WhenNonForeignKeyConstraintFails_Returns500AndLeavesTodoUnchanged_AC073()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var contactId = await TodoStoreTests.CreateContactAsync(client, "Trigger", $"trigger.upd.{Guid.NewGuid():N}@example.com");
        var created = await client.PostAsJsonAsync("/api/todos", new { title = "Pre-existing", contactId }, Ct);
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        using var createdDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync(Ct));
        var id = createdDoc.RootElement.GetProperty("id").GetGuid();
        var before = await SnapshotAsync();
        var rowBefore = await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, id);

        await factory.ExecuteSqlAsync(
            "CREATE TRIGGER trg_fail_update_todo BEFORE UPDATE ON Todos BEGIN SELECT RAISE(ABORT, 'x'); END");
        HttpResponseMessage response;
        string body;
        try
        {
            response = await client.PutAsJsonAsync($"/api/todos/{id}", new { title = "Doomed", contactId }, Ct);
            body = await response.Content.ReadAsStringAsync(Ct);
        }
        finally
        {
            await factory.ExecuteSqlAsync("DROP TRIGGER IF EXISTS trg_fail_update_todo");
        }

        Assert.Equal(500, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(500, problem.RootElement.GetProperty("status").GetInt32());
        Assert.False(problem.RootElement.TryGetProperty("errors", out _), "A non-FK failure must not become a validation error");
        Assert.DoesNotContain("existing contact", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contactId", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Sqlite", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
        Assert.DoesNotContain("RAISE", body, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(rowBefore, await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, id));
    }

    // AC-073: a rejected UPDATE during complete is a safe 500 and the to-do stays open and unchanged.
    [Fact]
    public async Task CompleteTodo_WhenUpdateRejected_Returns500AndLeavesTodoOpen_AC073()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/todos", new { title = "Pre-existing" }, Ct);
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        using var createdDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync(Ct));
        var id = createdDoc.RootElement.GetProperty("id").GetGuid();
        var rowBefore = await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, id);
        factory.Time.Advance(TimeSpan.FromHours(1));

        await factory.ExecuteSqlAsync(
            "CREATE TRIGGER trg_fail_complete_todo BEFORE UPDATE ON Todos BEGIN SELECT RAISE(ABORT, 'x'); END");
        HttpResponseMessage response;
        string body;
        try
        {
            response = await client.PostAsync($"/api/todos/{id}/complete", null, Ct);
            body = await response.Content.ReadAsStringAsync(Ct);
        }
        finally
        {
            await factory.ExecuteSqlAsync("DROP TRIGGER IF EXISTS trg_fail_complete_todo");
        }

        Assert.Equal(500, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("Sqlite", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
        Assert.DoesNotContain("RAISE", body, StringComparison.OrdinalIgnoreCase);

        var rowAfter = await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, id);
        Assert.Equal(rowBefore, rowAfter);
        Assert.Equal(0L, Convert.ToInt64(rowAfter!["IsDone"]));
        Assert.Null(rowAfter["CompletedAt"]);
    }
}
