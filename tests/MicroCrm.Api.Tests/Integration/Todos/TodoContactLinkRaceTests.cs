using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using MicroCrm.Api.Data;
using MicroCrm.Api.Tests.Integration.Infrastructure;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace MicroCrm.Api.Tests.Integration.Todos;

// Races between linking a to-do and deleting its contact (spec 003, T-13; ADR-0008). The FK decides contact
// existence, so a contact deleted mid-request must give 400 contactId and never a stale link or a 500.
public sealed class TodoContactLinkRaceTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string MissingContactMessage = "Must refer to an existing contact.";

    private const string DanglingSql =
        "SELECT COUNT(*) FROM Todos t WHERE t.ContactId IS NOT NULL " +
        "AND NOT EXISTS (SELECT 1 FROM Contacts c WHERE c.Id = t.ContactId)";

    public async ValueTask InitializeAsync() => await factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Deletes the contact through a separate connection, once, in the middle of the handler's SaveChanges:
    // after any pre-check query would have run and before EF's INSERT/UPDATE reaches the store.
    private sealed class DeleteContactOnceInterceptor(string connectionString, Guid contactId) : SaveChangesInterceptor
    {
        private int _fired;

        public int Fired => Volatile.Read(ref _fired);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0)
            {
                await using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM Contacts WHERE lower(Id) = $id";
                command.Parameters.AddWithValue("$id", contactId.ToString().ToLowerInvariant());
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }
    }

    // Records the SQL text of every command the host sends, to prove no contact lookup precedes the save.
    private sealed class RecordingCommandInterceptor : DbCommandInterceptor
    {
        private readonly List<string> _commands = [];

        public string[] Commands
        {
            get
            {
                lock (_commands)
                {
                    return [.. _commands];
                }
            }
        }

        private void Record(DbCommand command)
        {
            lock (_commands)
            {
                _commands.Add(command.CommandText);
            }
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }
    }

    private WebApplicationFactory<Program> Raced(params IInterceptor[] interceptors) =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(interceptors))));

    private static Task<Guid> NewContactAsync(HttpClient client, string tag) =>
        TodoStoreTests.CreateContactAsync(client, "Race", $"race.{tag}.{Guid.NewGuid():N}@example.com");

    private static async Task<Guid> NewTodoAsync(HttpClient client, string title, Guid? contactId)
    {
        var response = await client.PostAsJsonAsync("/api/todos", new { title, notes = "Original notes", contactId }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<long> TodoCountAsync() =>
        await TodoStoreTests.ScalarAsync(factory.ConnectionString, "SELECT COUNT(*) FROM Todos");

    private async Task<long> DanglingCountAsync() =>
        await TodoStoreTests.ScalarAsync(factory.ConnectionString, DanglingSql);

    private async Task<long> ForeignKeyViolationRowsAsync()
    {
        await using var connection = await TodoStoreTests.OpenAsync(factory.ConnectionString);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check(Todos)";
        await using var reader = await command.ExecuteReaderAsync(Ct);
        long rows = 0;
        while (await reader.ReadAsync(Ct))
        {
            rows++;
        }

        return rows;
    }

    private static async Task<JsonDocument> GetTodoAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/todos/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
    }

    private static string[] ContactMessages(JsonDocument problem) =>
        [.. problem.RootElement.GetProperty("errors").GetProperty("contactId").EnumerateArray().Select(e => e.GetString()!)];

    // Guard: same path as AC-016 (FK 787 -> 400).
    [Fact]
    public async Task CreateTodo_ContactDeletedBeforeSave_Returns400AndCreatesNothing_AC067()
    {
        using var seed = factory.CreateClient();
        var contact = await NewContactAsync(seed, "create");
        var interceptor = new DeleteContactOnceInterceptor(factory.ConnectionString, contact);
        using var raced = Raced(interceptor);
        using var client = raced.CreateClient();
        var before = await TodoCountAsync();

        var response = await client.PostAsJsonAsync("/api/todos", new { title = "Doomed link", contactId = contact }, Ct);

        Assert.Equal(1, interceptor.Fired);
        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "contactId");
        Assert.Equal([MissingContactMessage], ContactMessages(problem));
        Assert.Equal(before, await TodoCountAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await seed.GetAsync($"/api/contacts/{contact}", Ct)).StatusCode);
    }

    // Guard: same path as AC-029 (FK 787 -> 400).
    [Fact]
    public async Task UpdateTodo_NewContactDeletedBeforeSave_Returns400AndLeavesTodoUnchanged_AC067()
    {
        using var seed = factory.CreateClient();
        var contact = await NewContactAsync(seed, "relink");
        var todo = await NewTodoAsync(seed, "Original title", null);
        using var before = await GetTodoAsync(seed, todo);
        var interceptor = new DeleteContactOnceInterceptor(factory.ConnectionString, contact);
        using var raced = Raced(interceptor);
        using var client = raced.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/todos/{todo}", new { title = "Changed title", notes = "Changed notes", contactId = contact }, Ct);

        Assert.Equal(1, interceptor.Fired);
        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "contactId");
        Assert.Equal([MissingContactMessage], ContactMessages(problem));
        using var after = await GetTodoAsync(seed, todo);
        Assert.Equal(before.RootElement.GetRawText(), after.RootElement.GetRawText());
    }

    // RED until PUT marks ContactId modified: EF omits the unchanged column, so the UPDATE succeeds and the
    // handler answers 200 with the contactId the store has already cleared.
    [Fact]
    public async Task UpdateTodo_KeptContactDeletedBeforeSave_Returns400AndTodoIsUnlinked_AC067()
    {
        using var seed = factory.CreateClient();
        var contact = await NewContactAsync(seed, "kept");
        var todo = await NewTodoAsync(seed, "Original title", contact);
        using var before = await GetTodoAsync(seed, todo);
        var interceptor = new DeleteContactOnceInterceptor(factory.ConnectionString, contact);
        using var raced = Raced(interceptor);
        using var client = raced.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/todos/{todo}", new { title = "Changed title", notes = "Original notes", contactId = contact }, Ct);

        Assert.Equal(1, interceptor.Fired);
        using var problem = await ProblemAssert.IsProblemAsync(response, 400, "contactId");
        Assert.Equal([MissingContactMessage], ContactMessages(problem));
        using var after = await GetTodoAsync(seed, todo);
        Assert.Equal(JsonValueKind.Null, after.RootElement.GetProperty("contactId").ValueKind);
        Assert.Equal("Original title", after.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            before.RootElement.GetProperty("updatedAt").GetRawText(),
            after.RootElement.GetProperty("updatedAt").GetRawText());
        Assert.Equal(0, await DanglingCountAsync());
    }

    // Guard (ADR-0008: no contact pre-check). The FK decides, so the only statements on the request are the
    // INSERT, and no query touches Contacts. A pre-check (AnyAsync) would show up here deterministically.
    [Fact]
    public async Task CreateTodo_WithContact_SendsNoContactLookup_AC067()
    {
        using var seed = factory.CreateClient();
        var contact = await NewContactAsync(seed, "nolookup-create");
        var recorder = new RecordingCommandInterceptor();
        using var raced = Raced(recorder);
        using var client = raced.CreateClient();

        var response = await client.PostAsJsonAsync("/api/todos", new { title = "Plain link", contactId = contact }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotEmpty(recorder.Commands);
        Assert.DoesNotContain(recorder.Commands, sql => sql.Contains("Contacts", StringComparison.OrdinalIgnoreCase));
    }

    // Guard, same rule on PUT: only the to-do lookup and the UPDATE; no query touches Contacts.
    [Fact]
    public async Task UpdateTodo_WithContact_SendsNoContactLookup_AC067()
    {
        using var seed = factory.CreateClient();
        var contact = await NewContactAsync(seed, "nolookup-update");
        var todo = await NewTodoAsync(seed, "Original title", null);
        var recorder = new RecordingCommandInterceptor();
        using var raced = Raced(recorder);
        using var client = raced.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/todos/{todo}", new { title = "Changed title", contactId = contact }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(recorder.Commands);
        Assert.DoesNotContain(recorder.Commands, sql => sql.Contains("Contacts", StringComparison.OrdinalIgnoreCase));
    }

    // Guard (the FK guarantees the invariants). Timing decides the interleaving; the assertions hold for all of them.
    [Fact]
    public async Task CreateUpdateAndContactDelete_Concurrent_NoDanglingLinks_AC067()
    {
        const int contacts = 6;
        const int createsPerContact = 3;
        const int putsPerContact = 3;
        using var seed = factory.CreateClient();
        var contactIds = new List<Guid>();
        var putTargets = new List<(Guid Contact, Guid Todo)>();
        for (var i = 0; i < contacts; i++)
        {
            var contact = await NewContactAsync(seed, $"conc{i}");
            contactIds.Add(contact);
            for (var j = 0; j < putsPerContact; j++)
            {
                putTargets.Add((contact, await NewTodoAsync(seed, $"Relink {i}.{j}", null)));
            }
        }

        var requests = contacts * (1 + createsPerContact + putsPerContact);
        var clients = Enumerable.Range(0, requests).Select(_ => factory.CreateClient()).ToArray();
        try
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ready = 0;
            var slot = -1;

            Task<HttpResponseMessage> Start(Func<HttpClient, Task<HttpResponseMessage>> send)
            {
                var client = clients[Interlocked.Increment(ref slot)];
                return Task.Run(async () =>
                {
                    if (Interlocked.Increment(ref ready) == clients.Length)
                    {
                        allReady.SetResult();
                    }

                    await gate.Task;
                    return await send(client);
                }, Ct);
            }

            var creates = new List<Task<HttpResponseMessage>>();
            var puts = new List<Task<HttpResponseMessage>>();
            var deletes = new List<Task<HttpResponseMessage>>();
            foreach (var contact in contactIds)
            {
                deletes.Add(Start(c => c.DeleteAsync($"/api/contacts/{contact}", Ct)));
                for (var k = 0; k < createsPerContact; k++)
                {
                    var title = $"Racing create {k}";
                    creates.Add(Start(c => c.PostAsJsonAsync("/api/todos", new { title, contactId = contact }, Ct)));
                }
            }

            foreach (var (contact, todo) in putTargets)
            {
                puts.Add(Start(c => c.PutAsJsonAsync(
                    $"/api/todos/{todo}", new { title = "Relinked", contactId = contact }, Ct)));
            }

            await allReady.Task.WaitAsync(Ct);
            gate.SetResult();
            var createResults = await Task.WhenAll(creates);
            var putResults = await Task.WhenAll(puts);
            var deleteResults = await Task.WhenAll(deletes);

            Assert.All(createResults, r => Assert.True(
                r.StatusCode is HttpStatusCode.Created or HttpStatusCode.BadRequest, $"POST returned {(int)r.StatusCode}"));
            Assert.All(putResults, r => Assert.True(
                r.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
                $"PUT returned {(int)r.StatusCode}"));
            Assert.All(deleteResults, r => Assert.True(
                r.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound, $"DELETE returned {(int)r.StatusCode}"));

            Assert.Equal(0, await DanglingCountAsync());
            Assert.Equal(0, await ForeignKeyViolationRowsAsync());
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    // Guard (structural: removal and unlink are one statement). A reader on its own connection must never see
    // a to-do pointing at a contact that is already gone. Reads that hit a shared-cache lock are retried.
    [Fact]
    public async Task DeleteContacts_ConcurrentObserver_NeverSeesDanglingLink_AC065()
    {
        const int contacts = 6;
        const int todosPerContact = 5;
        using var seed = factory.CreateClient();
        var contactIds = new List<Guid>();
        for (var i = 0; i < contacts; i++)
        {
            var contact = await NewContactAsync(seed, $"obs{i}");
            contactIds.Add(contact);
            for (var j = 0; j < todosPerContact; j++)
            {
                await NewTodoAsync(seed, $"Observed {i}.{j}", contact);
            }
        }

        Assert.Equal(0, await DanglingCountAsync());
        var clients = contactIds.Select(_ => factory.CreateClient()).ToArray();
        try
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var deletesDone = false;
            var reads = 0;
            var nonZeroReads = new List<long>();

            var reader = Task.Run(async () =>
            {
                await using var connection = await TodoStoreTests.OpenAsync(factory.ConnectionString);
                await using var command = connection.CreateCommand();
                command.CommandText = DanglingSql;
                var finishing = false;
                while (true)
                {
                    try
                    {
                        var value = Convert.ToInt64(await command.ExecuteScalarAsync(Ct));
                        reads++;
                        if (value != 0)
                        {
                            nonZeroReads.Add(value);
                        }

                        if (reads == 1)
                        {
                            gate.TrySetResult();
                        }
                    }
                    catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
                    {
                        // Busy or table-locked by the writer in shared-cache mode: retry the read.
                        await Task.Yield();
                    }

                    if (finishing)
                    {
                        return;
                    }

                    finishing = Volatile.Read(ref deletesDone);
                }
            }, Ct);

            await gate.Task.WaitAsync(Ct);
            var deletes = contactIds.Select((id, i) => Task.Run(
                () => clients[i].DeleteAsync($"/api/contacts/{id}", Ct), Ct)).ToArray();
            var results = await Task.WhenAll(deletes);
            Volatile.Write(ref deletesDone, true);
            await reader;

            Assert.All(results, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
            Assert.Empty(nonZeroReads);
            Assert.True(reads >= 2, "The observer should have completed reads before and after the deletes.");
            Assert.Equal(0, await DanglingCountAsync());
            Assert.Equal(0, await ForeignKeyViolationRowsAsync());
            Assert.Equal(contacts * todosPerContact, await TodoCountAsync());
            Assert.Equal(
                contacts * todosPerContact,
                await TodoStoreTests.ScalarAsync(factory.ConnectionString, "SELECT COUNT(*) FROM Todos WHERE ContactId IS NULL"));
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }
}
