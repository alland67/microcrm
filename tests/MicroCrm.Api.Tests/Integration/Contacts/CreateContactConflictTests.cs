using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroCrm.Api.Tests.Integration.Contacts;

// Email uniqueness is enforced by the database (ADR-0003); concurrency runs on a named shared-cache
// in-memory database with one connection per request (ADR-0005). Every test uses its own email so the
// shared class fixture needs no reset.
public sealed class CreateContactConflictTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const int SqliteConstraintUnique = 2067;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateContact_WithExistingEmailDifferentCaseAndWhitespace_Returns409Problem_AC011()
    {
        const string email = "ada@example.com";
        using var client = factory.CreateClient();
        var first = await client.PostAsJsonAsync("/api/contacts", new { firstName = "Ada", email }, Ct);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var response = await client.PostAsJsonAsync("/api/contacts", new { firstName = "Other", email = "  ADA@Example.COM " }, Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 409);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("ada@example.com", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await CountContactsWithEmailAsync(email));
    }

    [Fact]
    public async Task CreateContact_ManyWithoutEmail_AllSucceed_AC013()
    {
        const string firstName = "NoEmail-AC013";
        using var client = factory.CreateClient();
        var bodies = new[]
        {
            new Dictionary<string, string?> { ["firstName"] = firstName },
            new Dictionary<string, string?> { ["firstName"] = firstName },
            new Dictionary<string, string?> { ["firstName"] = firstName, ["email"] = null },
            new Dictionary<string, string?> { ["firstName"] = firstName, ["email"] = "" },
        };

        foreach (var body in bodies)
        {
            var response = await client.PostAsJsonAsync("/api/contacts", body, Ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        Assert.Equal(bodies.Length, await ScalarAsync("SELECT COUNT(*) FROM Contacts WHERE FirstName = $v AND Email IS NULL", firstName));
    }

    [Fact]
    public async Task Database_RejectsDuplicateEmailDifferingOnlyInCase_AC016()
    {
        using var client = factory.CreateClient(); // starts the host, which applies the migrations
        await using var connection = new SqliteConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await InsertAsync(connection, "Db.Dup@Example.com");

        var ex = await Assert.ThrowsAsync<SqliteException>(() => InsertAsync(connection, "db.dup@example.COM"));

        Assert.Equal(SqliteConstraintUnique, ex.SqliteExtendedErrorCode);
    }

    [Fact]
    public async Task CreateContact_ConcurrentSameEmail_ExactlyOneCreatedRestConflict_AC016()
    {
        const int requests = 10;
        const string email = "race@example.com";
        var clients = Enumerable.Range(0, requests).Select(_ => factory.CreateClient()).ToArray(); // host started before the race
        try
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ready = 0;

            var tasks = clients.Select((client, i) => Task.Run(async () =>
            {
                // Different casings of the same address; released together once all are waiting at the gate.
                var sent = i % 2 == 0 ? email : email.ToUpperInvariant();
                if (Interlocked.Increment(ref ready) == requests)
                {
                    allReady.SetResult();
                }

                await gate.Task;
                var response = await client.PostAsJsonAsync("/api/contacts", new { firstName = $"Racer{i}", email = sent }, Ct);
                var body = await response.Content.ReadAsStringAsync(Ct);
                return (Response: response, Body: body);
            }, Ct)).ToArray();

            await allReady.Task.WaitAsync(Ct);
            gate.SetResult();
            var results = await Task.WhenAll(tasks);

            Assert.DoesNotContain(results, r => (int)r.Response.StatusCode >= 500);
            Assert.Single(results, r => r.Response.StatusCode == HttpStatusCode.Created);
            var conflicts = results.Where(r => r.Response.StatusCode == HttpStatusCode.Conflict).ToArray();
            Assert.Equal(requests - 1, conflicts.Length);
            foreach (var conflict in conflicts)
            {
                using var problem = await ProblemAssert.IsProblemAsync(conflict.Response, 409);
            }

            Assert.Equal(1, await CountContactsWithEmailAsync(email));
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    [Fact]
    public async Task CreateContact_DuplicateEmail_EmailNotLoggedAtInformationOrAbove_NFR004()
    {
        const string marker = "nfr004-secret-marker@example.com";
        var logs = new CapturingLoggerProvider();
        using var logged = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(logs)));
        using var client = logged.CreateClient();

        var first = await client.PostAsJsonAsync("/api/contacts", new { firstName = "Log", email = marker }, Ct);
        var duplicate = await client.PostAsJsonAsync("/api/contacts", new { firstName = "Log2", email = $"  {marker.ToUpperInvariant()} " }, Ct);

        // The duplicate's status (409) is pinned by AC-011; here it only has to be provoked.
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        _ = duplicate;

        // Canary: capture is live, and EF's command log (the place a value could leak) is at Information.
        // With sensitive-data logging off, parameter values render as '?', so the marker must not appear.
        Assert.Contains(logs.Entries, e => e.Category == "Microsoft.EntityFrameworkCore.Database.Command" && e.Level >= LogLevel.Information);
        foreach (var (category, level, text) in logs.Entries.Where(e => e.Level >= LogLevel.Information))
        {
            Assert.False(
                text.Contains(marker, StringComparison.OrdinalIgnoreCase),
                $"Email appeared in a {level} log entry from {category}.");
        }
    }

    private static async Task InsertAsync(SqliteConnection connection, string email)
    {
        await using var command = connection.CreateCommand();
        // Every NOT NULL column of the current schema: Id, FirstName, CreatedAt, UpdatedAt.
        command.CommandText =
            "INSERT INTO Contacts (Id, FirstName, Email, CreatedAt, UpdatedAt) VALUES ($id, 'Direct', $email, $ts, $ts)";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D").ToUpperInvariant());
        command.Parameters.AddWithValue("$email", email);
        command.Parameters.AddWithValue("$ts", "2026-01-02T03:04:05.0000000+00:00");
        await command.ExecuteNonQueryAsync(Ct);
    }

    private Task<long> CountContactsWithEmailAsync(string email) =>
        ScalarAsync("SELECT COUNT(*) FROM Contacts WHERE lower(Email) = $v", email.ToLowerInvariant());

    private async Task<long> ScalarAsync(string sql, string value)
    {
        await using var connection = new SqliteConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$v", value);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(string Category, LogLevel Level, string Text)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentQueue<(string, LogLevel, string)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                // Message, structured state values, and the full exception text (including inner exceptions).
                var text = formatter(state, exception) + "\n" + state + "\n" + exception;
                entries.Enqueue((category, logLevel, text));
            }
        }
    }
}
