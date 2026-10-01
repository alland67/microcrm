using Microsoft.AspNetCore.Mvc.Testing;

namespace MicroCrm.Api.Tests.Integration;

// Harness smoke test: proves the API host boots under WebApplicationFactory.
public sealed class SmokeTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task OpenApiDocument_IsServed_InDevelopment()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Expected 2xx, got {(int)response.StatusCode}");
    }
}
