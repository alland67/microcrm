using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

namespace MicroCrm.Api.Tests.Integration.Todos;

// Overdue flips at UTC midnight (spec 003, T-11). Own fixture: this test moves the shared fake clock.
public sealed class OverdueClockTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListTodos_Overdue_IncludesTodoDueYesterdayAfterUtcMidnight_AC050()
    {
        await factory.ResetAsync();
        using var client = factory.CreateClient();
        var day = DateOnly.FromDateTime(factory.Time.GetUtcNow().UtcDateTime);
        var endOfDay = new DateTimeOffset(day.Year, day.Month, day.Day, 23, 59, 59, TimeSpan.Zero);
        factory.Time.SetUtcNow(endOfDay);
        var created = await client.PostAsJsonAsync(
            "/api/todos", new { title = "Due today", dueDate = day.ToString("yyyy-MM-dd") }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync(Ct));
        var id = createdDoc.RootElement.GetProperty("id").GetGuid();

        Assert.DoesNotContain(id, await OverdueIdsAsync(client));

        factory.Time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(day.AddDays(1), DateOnly.FromDateTime(factory.Time.GetUtcNow().UtcDateTime));
        Assert.Contains(id, await OverdueIdsAsync(client));
    }

    private static async Task<Guid[]> OverdueIdsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/todos?status=overdue", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToArray();
    }
}
