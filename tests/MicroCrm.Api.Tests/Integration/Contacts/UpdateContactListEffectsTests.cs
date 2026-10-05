using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

namespace MicroCrm.Api.Tests.Integration.Contacts;

public sealed class UpdateContactListEffectsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task<Guid> CreateAsync(HttpClient client, string first, string? last, string? email)
    {
        var response = await client.PostAsJsonAsync("/api/contacts", new { firstName = first, lastName = last, email }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement[]> ListAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync($"/api/contacts{query}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("items").EnumerateArray().ToArray();
    }

    [Fact]
    public async Task UpdateContact_ChangedNames_ReorderListByNewValues_AC008()
    {
        using var client = factory.CreateClient();
        await CreateAsync(client, "Alice", "Adams", "adams.ac008@example.com");
        var moving = await CreateAsync(client, "Bob", "Baker", "baker.ac008@example.com");
        await CreateAsync(client, "Carl", "Clark", "clark.ac008@example.com");
        await CreateAsync(client, "Dana", "Clark", "dana.ac008@example.com");

        // Last name change: Baker -> Zimmer moves to the end.
        var put1 = await client.PutAsJsonAsync(
            $"/api/contacts/{moving}",
            new { firstName = "Bob", lastName = "Zimmer", email = "baker.ac008@example.com" },
            Ct);
        Assert.Equal(HttpStatusCode.OK, put1.StatusCode);
        var afterLast = (await ListAsync(client)).Select(i => i.GetProperty("email").GetString()).ToArray();
        Assert.Equal(
            new[]
            {
                "adams.ac008@example.com",
                "clark.ac008@example.com",
                "dana.ac008@example.com",
                "baker.ac008@example.com",
            },
            afterLast);

        // First name change within the same last name: Carl -> Zed sorts after Dana (Clark, Zed).
        var carl = (await ListAsync(client))
            .Single(i => i.GetProperty("email").GetString() == "clark.ac008@example.com")
            .GetProperty("id").GetGuid();
        var put2 = await client.PutAsJsonAsync(
            $"/api/contacts/{carl}",
            new { firstName = "Zed", lastName = "Clark", email = "clark.ac008@example.com" },
            Ct);
        Assert.Equal(HttpStatusCode.OK, put2.StatusCode);
        var afterFirst = (await ListAsync(client)).Select(i => i.GetProperty("email").GetString()).ToArray();
        Assert.Equal(
            new[]
            {
                "adams.ac008@example.com",
                "dana.ac008@example.com",
                "clark.ac008@example.com",
                "baker.ac008@example.com",
            },
            afterFirst);
    }

    [Fact]
    public async Task UpdateContact_ChangedEmail_SearchFindsNewNotOld_AC009()
    {
        using var client = factory.CreateClient();
        var id = await CreateAsync(client, "Search", "Subject", "oldaddr.ac009@example.com");
        await CreateAsync(client, "Other", "Person", "other.ac009@example.com");

        // Precondition: old email is findable before the update.
        Assert.Single(await ListAsync(client, "?search=oldaddr.ac009"));

        var put = await client.PutAsJsonAsync(
            $"/api/contacts/{id}",
            new { firstName = "Search", lastName = "Subject", email = "newaddr.ac009@example.com" },
            Ct);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        using var reader = factory.CreateClient();
        var found = Assert.Single(await ListAsync(reader, "?search=newaddr.ac009"));
        Assert.Equal(id, found.GetProperty("id").GetGuid());
        Assert.Empty(await ListAsync(reader, "?search=oldaddr.ac009"));
    }

    [Fact]
    public async Task UpdateContact_LeavesOtherContactsUnchanged_AC011()
    {
        using var client = factory.CreateClient();
        var target = await CreateAsync(client, "Target", "Tee", "target.ac011@example.com");
        await CreateAsync(client, "Other", "One", "one.ac011@example.com");
        await CreateAsync(client, "Other", "Two", "two.ac011@example.com");
        await CreateAsync(client, "Other", "Three", null);
        var before = (await ListAsync(client))
            .Where(i => i.GetProperty("id").GetGuid() != target)
            .ToDictionary(i => i.GetProperty("id").GetGuid(), i => i.GetRawText());
        Assert.Equal(3, before.Count);
        factory.Time.Advance(TimeSpan.FromMinutes(10));

        var put = await client.PutAsJsonAsync(
            $"/api/contacts/{target}",
            new { firstName = "Changed", lastName = "Tee", email = "changed.ac011@example.com" },
            Ct);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var afterAll = await ListAsync(client);
        Assert.Equal(4, afterAll.Length);
        var after = afterAll
            .Where(i => i.GetProperty("id").GetGuid() != target)
            .ToDictionary(i => i.GetProperty("id").GetGuid(), i => i.GetRawText());
        Assert.Equal(before, after);
    }
}
