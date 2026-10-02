using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace MicroCrm.Api.Tests.Integration.Infrastructure;

// Test host for integration tests (ADR-0005): Development environment, a unique named shared-cache
// in-memory SQLite database (kept alive by one open connection), and a controllable clock.
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _keepAlive;

    public ApiFactory()
    {
        ConnectionString = $"Data Source=microcrm-test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

        // Opened before the host starts so the in-memory database outlives per-request connections.
        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();
    }

    public string ConnectionString { get; }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

    /// <summary>Runs SQL on a fresh connection to the test database (setup, fault injection, inspection).</summary>
    public async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:MicroCrm", ConnectionString);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _keepAlive.Dispose();
            SqliteConnection.ClearPool(new SqliteConnection(ConnectionString));
        }

        base.Dispose(disposing);
    }
}
