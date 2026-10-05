using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroCrm.Api.Tests.Integration.Contacts;

// Email uniqueness on update is enforced by the database (ADR-0003), as on create. Every test uses its own
// emails so the shared class fixture needs no reset.
public sealed class UpdateContactConflictTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> CreateAsync(HttpClient client, string first, string? email)
    {
        var response = await client.PostAsJsonAsync("/api/contacts", new { firstName = first, email }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, string first, string? email)
        => client.PutAsJsonAsync($"/api/contacts/{id}", new { firstName = first, email }, Ct);

    private async Task<string?> StoredEmailAsync(Guid id)
    {
        await using var connection = new SqliteConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Email FROM Contacts WHERE lower(Id) = $id";
        command.Parameters.AddWithValue("$id", id.ToString().ToLowerInvariant());
        var value = await command.ExecuteScalarAsync(Ct);
        return value is DBNull or null ? null : (string)value;
    }

    private async Task<long> CountWithEmailAsync(string email)
    {
        await using var connection = new SqliteConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Contacts WHERE Email = $v COLLATE NOCASE";
        command.Parameters.AddWithValue("$v", email);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    // Guard: the unique index already allows this at RED.
    [Fact]
    public async Task UpdateContact_SameEmailDifferentCaseOrWhitespace_Returns200_AC012()
    {
        using var client = factory.CreateClient();
        var id = await CreateAsync(client, "Self", "self.ac012@example.com");
        await CreateAsync(client, "Peer", "peer.ac012@example.com");

        foreach (var sent in new[] { "self.ac012@example.com", "  SELF.ac012@EXAMPLE.com ", "Self.AC012@example.com" })
        {
            var response = await PutAsync(client, id, "Self", sent);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // Guard.
    [Fact]
    public async Task UpdateContact_ChangeOnlyCaseOfOwnEmail_Returns200AndStoresNewCasing_AC013()
    {
        using var client = factory.CreateClient();
        var id = await CreateAsync(client, "Case", "case.ac013@example.com");

        var response = await PutAsync(client, id, "Case", "Case.AC013@Example.com");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("Case.AC013@Example.com", doc.RootElement.GetProperty("email").GetString());
        Assert.Equal("Case.AC013@Example.com", await StoredEmailAsync(id));
    }

    [Fact]
    public async Task UpdateContact_EmailOfAnotherContact_Returns409ProblemAndLeavesContactUnchanged_AC014()
    {
        using var client = factory.CreateClient();
        var target = await CreateAsync(client, "Target", "target.ac014@example.com");
        await CreateAsync(client, "Holder", "holder.ac014@example.com");

        var response = await PutAsync(client, target, "Changed", "  HOLDER.ac014@Example.COM ");

        using var problem = await ProblemAssert.IsProblemAsync(response, 409);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("holder.ac014@example.com", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("target.ac014@example.com", await StoredEmailAsync(target));
        using var reader = factory.CreateClient();
        using var fetched = JsonDocument.Parse(await reader.GetStringAsync($"/api/contacts/{target}", Ct));
        Assert.Equal("Target", fetched.RootElement.GetProperty("firstName").GetString());
        Assert.Equal(1, await CountWithEmailAsync("holder.ac014@example.com"));
    }

    // Guard.
    [Fact]
    public async Task UpdateContact_ToNoEmail_AlwaysSucceeds_AC015()
    {
        using var client = factory.CreateClient();
        var ids = new[]
        {
            await CreateAsync(client, "NoMail-AC015", "a.ac015@example.com"),
            await CreateAsync(client, "NoMail-AC015", "b.ac015@example.com"),
            await CreateAsync(client, "NoMail-AC015", "c.ac015@example.com"),
        };

        foreach (var id in ids)
        {
            var response = await PutAsync(client, id, "NoMail-AC015", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        foreach (var id in ids)
        {
            Assert.Null(await StoredEmailAsync(id));
        }
    }

    // Guard.
    [Fact]
    public async Task UpdateContact_OldEmailCanBeReusedAfterChangeOrRemoval_AC016()
    {
        using var client = factory.CreateClient();
        var changed = await CreateAsync(client, "Changed", "changed.old.ac016@example.com");
        var removed = await CreateAsync(client, "Removed", "removed.old.ac016@example.com");
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(client, changed, "Changed", "changed.new.ac016@example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(client, removed, "Removed", null)).StatusCode);

        // By create, using the changed contact's old email in another casing.
        var created = await client.PostAsJsonAsync(
            "/api/contacts",
            new { firstName = "Newcomer", email = "CHANGED.old.ac016@example.com" },
            Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // By update, using the removed contact's old email.
        var third = await CreateAsync(client, "Third", "third.ac016@example.com");
        var updated = await PutAsync(client, third, "Third", "removed.old.ac016@example.com");
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
    }

    [Fact]
    public async Task CreateAndUpdate_ConcurrentSameEmail_AtMostOneHoldsIt_AC034()
    {
        const int updaters = 5;
        const int creators = 5;
        const string email = "race.ac034@example.com";
        using var seedClient = factory.CreateClient();
        var seeded = new List<(Guid Id, string Original)>();
        for (var i = 0; i < updaters; i++)
        {
            var original = $"orig{i}.ac034@example.com";
            seeded.Add((await CreateAsync(seedClient, $"Seed{i}", original), original));
        }

        var total = updaters + creators;
        var clients = Enumerable.Range(0, total).Select(_ => factory.CreateClient()).ToArray();
        try
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ready = 0;

            var tasks = clients.Select((client, i) => Task.Run(async () =>
            {
                var sent = i % 2 == 0 ? email : email.ToUpperInvariant();
                if (Interlocked.Increment(ref ready) == total)
                {
                    allReady.SetResult();
                }

                await gate.Task;
                var isUpdate = i < updaters;
                var response = isUpdate
                    ? await PutAsync(client, seeded[i].Id, $"Seed{i}", sent)
                    : await client.PostAsJsonAsync("/api/contacts", new { firstName = $"Creator{i}", email = sent }, Ct);
                return (IsUpdate: isUpdate, Index: i, Response: response);
            }, Ct)).ToArray();

            await allReady.Task.WaitAsync(Ct);
            gate.SetResult();
            var results = await Task.WhenAll(tasks);

            Assert.DoesNotContain(results, r => (int)r.Response.StatusCode >= 500);
            Assert.Single(results, r => r.Response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created);
            foreach (var r in results.Where(r => r.Response.StatusCode == HttpStatusCode.Conflict))
            {
                using var problem = await ProblemAssert.IsProblemAsync(r.Response, 409);
            }

            Assert.Equal(total - 1, results.Count(r => r.Response.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(1, await CountWithEmailAsync(email));
            foreach (var loser in results.Where(r => r.IsUpdate && r.Response.StatusCode == HttpStatusCode.Conflict))
            {
                Assert.Equal(seeded[loser.Index].Original, await StoredEmailAsync(seeded[loser.Index].Id));
            }
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    // Guard (reviewer also inspects logging code).
    [Fact]
    public async Task UpdateContact_DuplicateEmail_EmailNotLoggedAtInformationOrAbove_NFR003()
    {
        const string marker = "nfr003-secret-marker@example.com";
        var logs = new CapturingLoggerProvider();
        using var logged = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(logs)));
        using var client = logged.CreateClient();
        await CreateAsync(client, "Holder", marker);
        var other = await CreateAsync(client, "Other", "other.nfr003@example.com");

        // The duplicate's status (409) is pinned by AC-014; here it only has to be provoked.
        _ = await PutAsync(client, other, "Other", $"  {marker.ToUpperInvariant()} ");

        Assert.Contains(logs.Entries, e => e.Category == "Microsoft.EntityFrameworkCore.Database.Command" && e.Level >= LogLevel.Information);
        foreach (var (category, level, text) in logs.Entries.Where(e => e.Level >= LogLevel.Information))
        {
            Assert.False(
                text.Contains(marker, StringComparison.OrdinalIgnoreCase),
                $"Email appeared in a {level} log entry from {category}.");
        }
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
                var text = formatter(state, exception) + "\n" + state + "\n" + exception;
                entries.Enqueue((category, logLevel, text));
            }
        }
    }
}
