using System.Net.Http.Json;

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
}
