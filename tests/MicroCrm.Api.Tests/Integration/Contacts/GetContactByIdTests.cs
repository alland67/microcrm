using MicroCrm.Api.Tests.Integration.Infrastructure;

namespace MicroCrm.Api.Tests.Integration.Contacts;

public sealed class GetContactByIdTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("123")]
    public async Task GetContact_WithNonGuidId_Returns404Problem_AC019(string id)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/contacts/{id}", TestContext.Current.CancellationToken);

        // AC-019 (404) and AC-037 (problem+json with type/title/status)
        using var problem = await ProblemAssert.IsProblemAsync(response, 404);
    }
}
