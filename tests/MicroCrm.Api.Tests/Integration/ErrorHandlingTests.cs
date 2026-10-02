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
    public async Task CreateContact_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC038()
    {
        await BreakDatabaseAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/contacts", new { firstName = "Ada" }, Ct);

        await AssertSafe500Async(response);
    }
}
