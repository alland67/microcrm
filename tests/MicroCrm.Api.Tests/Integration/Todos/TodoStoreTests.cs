using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using MicroCrm.Api.Data;
using MicroCrm.Api.Tests.Integration.Infrastructure;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MicroCrm.Api.Tests.Integration.Todos;

// Store-level tests for the Todos table (plan.md "Schema", ADR-0008, ADR-0009). They use raw SQL on
// purpose: the rules under test are enforced by the database, not by API code. Unique data per test.
public sealed class TodoStoreTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Fixed tick values (UTC ticks, ADR-0009).
    private const long Created = 639_000_000_000_000_000L;
    private const long Updated = 639_000_000_100_000_000L;
    private const long Completed = 639_000_000_200_000_000L;

    // ---- helpers (internal: shared with ContactDeleteAtomicityTests) ----

    internal static async Task<Guid> CreateContactAsync(HttpClient client, string first, string email)
    {
        var response = await client.PostAsJsonAsync("/api/contacts", new { firstName = first, email }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    // EF stores GUIDs as uppercase canonical text.
    internal static string Upper(Guid id) => id.ToString().ToUpperInvariant();

    internal static async Task<SqliteConnection> OpenAsync(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    internal static async Task InsertTodoAsync(
        string connectionString,
        Guid id,
        string? contactId,
        bool isDone = false,
        long? completedAt = null,
        string title = "Seeded",
        string? notes = "Some notes",
        string? dueDate = "2026-10-05")
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO Todos (Id, Title, Notes, DueDate, ContactId, IsDone, CompletedAt, CreatedAt, UpdatedAt) " +
            "VALUES ($id, $title, $notes, $due, $contact, $done, $completed, $created, $updated)";
        command.Parameters.AddWithValue("$id", Upper(id));
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$notes", (object?)notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$due", (object?)dueDate ?? DBNull.Value);
        command.Parameters.AddWithValue("$contact", (object?)contactId ?? DBNull.Value);
        command.Parameters.AddWithValue("$done", isDone ? 1 : 0);
        command.Parameters.AddWithValue("$completed", (object?)completedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", Created);
        command.Parameters.AddWithValue("$updated", Updated);
        await command.ExecuteNonQueryAsync(Ct);
    }

    // Reads a to-do row as column name -> value (null for SQL NULL); null when the row doesn't exist.
    internal static async Task<Dictionary<string, object?>?> ReadTodoAsync(string connectionString, Guid id)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Todos WHERE lower(Id) = $id";
        command.Parameters.AddWithValue("$id", id.ToString().ToLowerInvariant());
        await using var reader = await command.ExecuteReaderAsync(Ct);
        if (!await reader.ReadAsync(Ct))
        {
            return null;
        }

        var row = new Dictionary<string, object?>();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            row[reader.GetName(i)] = await reader.IsDBNullAsync(i, Ct) ? null : reader.GetValue(i);
        }

        return row;
    }

    internal static async Task<long> ScalarAsync(string connectionString, string sql, params (string Name, object Value)[] args)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in args)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync(Ct));
    }

    private async Task<List<Dictionary<string, object?>>> PragmaAsync(string sql)
    {
        await using var connection = await OpenAsync(factory.ConnectionString);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(Ct))
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = await reader.IsDBNullAsync(i, Ct) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    private async Task StartHostAsync()
    {
        // Forces the host to start so migrations have run.
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/contacts", Ct)).StatusCode);
    }

    // ---- tests ----

    [Fact]
    public async Task Store_TodosColumns_MatchPlannedTypes_AC044()
    {
        await StartHostAsync();

        var columns = (await PragmaAsync("PRAGMA table_info(Todos)"))
            .ToDictionary(r => (string)r["name"]!, r => (Type: (string)r["type"]!, NotNull: Convert.ToInt64(r["notnull"]) == 1));

        var expected = new Dictionary<string, (string Type, bool NotNull)>
        {
            ["Id"] = ("TEXT", true),
            ["Title"] = ("TEXT", true),
            ["Notes"] = ("TEXT", false),
            ["DueDate"] = ("TEXT", false),
            ["ContactId"] = ("TEXT", false),
            ["IsDone"] = ("INTEGER", true),
            ["CompletedAt"] = ("INTEGER", false),
            ["CreatedAt"] = ("INTEGER", true),
            ["UpdatedAt"] = ("INTEGER", true),
        };

        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), columns.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, want) in expected)
        {
            Assert.Equal(want, (columns[name].Type.ToUpperInvariant(), columns[name].NotNull));
        }
    }

    [Fact]
    public async Task Store_TodosContactId_IsForeignKeyToContactsWithSetNull_AC066()
    {
        await StartHostAsync();

        var keys = await PragmaAsync("PRAGMA foreign_key_list(Todos)");

        var key = Assert.Single(keys);
        Assert.Equal("Contacts", (string)key["table"]!);
        Assert.Equal("ContactId", (string)key["from"]!);
        Assert.Equal("Id", (string)key["to"]!);
        Assert.Equal("SET NULL", (string)key["on_delete"]!);
    }

    [Fact]
    public async Task Store_TodosIndexes_SupportUnlinkAndFilters_NFR003()
    {
        await StartHostAsync();

        var indexes = (await PragmaAsync("PRAGMA index_list(Todos)")).Select(r => (string)r["name"]!).ToArray();
        Assert.Contains("IX_Todos_ContactId", indexes);
        Assert.Contains("IX_Todos_IsDone_DueDate", indexes);

        static string[] Columns(IEnumerable<Dictionary<string, object?>> info) =>
            [.. info.OrderBy(r => Convert.ToInt64(r["seqno"])).Select(r => (string)r["name"]!)];

        Assert.Equal(["ContactId"], Columns(await PragmaAsync("PRAGMA index_info(IX_Todos_ContactId)")));
        Assert.Equal(["IsDone", "DueDate"], Columns(await PragmaAsync("PRAGMA index_info(IX_Todos_IsDone_DueDate)")));
    }

    // Guard: passes at RED because the bundled native SQLite defaults foreign_keys to on (ADR-0008).
    [Fact]
    public async Task Store_TestConnections_HaveForeignKeysOn_AC066()
    {
        await using var connection = await OpenAsync(factory.ConnectionString);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys";

        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync(Ct)));
    }

    [Fact]
    public async Task Store_DeletingContactDirectly_UnlinksTodosAndKeepsOtherColumns_AC066()
    {
        using var client = factory.CreateClient();
        var contactId = await CreateContactAsync(client, "Direct", "direct.store.ac066@example.com");
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        await InsertTodoAsync(factory.ConnectionString, first, Upper(contactId), title: "Open one");
        await InsertTodoAsync(factory.ConnectionString, second, Upper(contactId), isDone: true, completedAt: Completed, title: "Done one", dueDate: null, notes: null);
        var before1 = await ReadTodoAsync(factory.ConnectionString, first);
        var before2 = await ReadTodoAsync(factory.ConnectionString, second);

        await factory.ExecuteSqlAsync($"DELETE FROM Contacts WHERE lower(Id) = '{contactId.ToString().ToLowerInvariant()}'");

        var after1 = await ReadTodoAsync(factory.ConnectionString, first);
        var after2 = await ReadTodoAsync(factory.ConnectionString, second);
        Assert.NotNull(after1);
        Assert.NotNull(after2);
        Assert.Null(after1["ContactId"]);
        Assert.Null(after2["ContactId"]);
        Assert.Equal(before1!.Where(kv => kv.Key != "ContactId"), after1.Where(kv => kv.Key != "ContactId"));
        Assert.Equal(before2!.Where(kv => kv.Key != "ContactId"), after2.Where(kv => kv.Key != "ContactId"));
    }

    [Fact]
    public async Task Store_LinkToMissingContact_IsRejectedByStore_AC067()
    {
        await StartHostAsync();

        var ex = await Assert.ThrowsAsync<SqliteException>(() =>
            InsertTodoAsync(factory.ConnectionString, Guid.CreateVersion7(), Upper(Guid.CreateVersion7())));

        Assert.Equal(787, ex.SqliteExtendedErrorCode);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, Completed)]
    public async Task Store_InconsistentDoneState_IsRejectedByStore_AC069(bool isDone, long? completedAt)
    {
        await StartHostAsync();

        var ex = await Assert.ThrowsAsync<SqliteException>(() =>
            InsertTodoAsync(factory.ConnectionString, Guid.CreateVersion7(), null, isDone, completedAt));

        Assert.Equal(275, ex.SqliteExtendedErrorCode);
    }

    [Fact]
    public async Task DeleteContact_WithLinkedTodos_Returns204AndUnlinks_AC062()
    {
        using var client = factory.CreateClient();
        var contactId = await CreateContactAsync(client, "Linked", "linked.ac062@example.com");
        var todoA = Guid.CreateVersion7();
        var todoB = Guid.CreateVersion7();
        await InsertTodoAsync(factory.ConnectionString, todoA, Upper(contactId));
        await InsertTodoAsync(factory.ConnectionString, todoB, Upper(contactId));

        var response = await client.DeleteAsync($"/api/contacts/{contactId}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        foreach (var id in new[] { todoA, todoB })
        {
            var row = await ReadTodoAsync(factory.ConnectionString, id);
            Assert.NotNull(row);
            Assert.Null(row["ContactId"]);
        }
    }

    [Fact]
    public async Task Api_ForeignKeysDisabledInConnectionString_StillEnforced_AC066()
    {
        using var seedClient = factory.CreateClient();
        var contactId = await CreateContactAsync(seedClient, "NoFk", "nofk.ac066@example.com");
        var todoId = Guid.CreateVersion7();
        await InsertTodoAsync(factory.ConnectionString, todoId, Upper(contactId));

        using var derived = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:MicroCrm", factory.ConnectionString + ";Foreign Keys=False"));
        using var client = derived.CreateClient();

        // (a) the API's own connection has foreign keys on despite the connection string.
        using (var scope = derived.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync(Ct);
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA foreign_keys";
                Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync(Ct)));
            }
            finally
            {
                await connection.CloseAsync();
            }
        }

        // (b) deleting through the derived host still unlinks the to-do.
        var response = await client.DeleteAsync($"/api/contacts/{contactId}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var row = await ReadTodoAsync(factory.ConnectionString, todoId);
        Assert.NotNull(row);
        Assert.Null(row["ContactId"]);
    }
}
