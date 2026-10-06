using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

namespace MicroCrm.Api.Tests.Integration.Todos;

// Deleting a contact keeps its to-dos and sets contactId to null (spec 003, T-04; ADR-0008).
// Every test asserts the to-do is linked before the contact is deleted, so a missing link fails loudly.
public sealed class ContactDeleteUnlinksTodosTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Guid> NewContactAsync(HttpClient client, string tag) =>
        await TodoStoreTests.CreateContactAsync(client, "Unlink", $"unlink.{tag}.{Guid.NewGuid():N}@example.com");

    private static async Task<Guid> CreateTodoAsync(
        HttpClient client, string title, Guid? contactId, string? notes = null, string? dueDate = null)
    {
        var response = await client.PostAsJsonAsync("/api/todos", new { title, notes, dueDate, contactId }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<string> GetBodyAsync(HttpClient client, Guid todoId)
    {
        var response = await client.GetAsync($"/api/todos/{todoId}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private static async Task AssertLinkedAsync(HttpClient client, string connectionString, Guid todoId, Guid contactId)
    {
        using var doc = JsonDocument.Parse(await GetBodyAsync(client, todoId));
        Assert.Equal(contactId.ToString(), doc.RootElement.GetProperty("contactId").GetString());
        var row = await TodoStoreTests.ReadTodoAsync(connectionString, todoId);
        Assert.NotNull(row);
        Assert.Equal(TodoStoreTests.Upper(contactId), row["ContactId"]);
    }

    private static Dictionary<string, JsonElement> Members(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public async Task DeleteContact_WithLinkedTodos_Returns204_AC062()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var contactId = await NewContactAsync(client, "ac062");
        var todoId = await CreateTodoAsync(client, "Linked", contactId);
        await AssertLinkedAsync(client, factory.ConnectionString, todoId, contactId);

        var response = await client.DeleteAsync($"/api/contacts/{contactId}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteContact_LinkedTodosRemainWithNullContactAndOtherFieldsUnchanged_AC063()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var contactId = await NewContactAsync(client, "ac063");
        var first = await CreateTodoAsync(client, "First", contactId, "Some notes", "2026-10-05");
        var second = await CreateTodoAsync(client, "Second", contactId);
        await AssertLinkedAsync(client, factory.ConnectionString, first, contactId);
        await AssertLinkedAsync(client, factory.ConnectionString, second, contactId);
        var beforeFirst = Members(await GetBodyAsync(client, first));
        var beforeSecond = Members(await GetBodyAsync(client, second));
        factory.Time.Advance(TimeSpan.FromHours(5));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/contacts/{contactId}", Ct)).StatusCode);

        using var fresh = factory.CreateClient();
        foreach (var (id, before) in new[] { (first, beforeFirst), (second, beforeSecond) })
        {
            var after = Members(await GetBodyAsync(fresh, id));
            Assert.Equal(JsonValueKind.Null, after["contactId"].ValueKind);
            Assert.Equal(before.Keys.Order().ToArray(), after.Keys.Order().ToArray());
            foreach (var (name, value) in before.Where(kv => kv.Key != "contactId"))
            {
                Assert.Equal(value.GetRawText(), after[name].GetRawText());
            }
        }
    }

    [Fact]
    public async Task DeleteContact_LeavesOtherContactsTodosAndUnlinkedTodosUnchanged_AC064()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var deleted = await NewContactAsync(client, "ac064a");
        var other = await NewContactAsync(client, "ac064b");
        var ofDeleted = await CreateTodoAsync(client, "Deleted contact's", deleted);
        var ofOther = await CreateTodoAsync(client, "Other contact's", other, "n", "2026-11-01");
        var unlinked = await CreateTodoAsync(client, "Unlinked", null, "m");
        await AssertLinkedAsync(client, factory.ConnectionString, ofDeleted, deleted);
        await AssertLinkedAsync(client, factory.ConnectionString, ofOther, other);
        var beforeOther = await GetBodyAsync(client, ofOther);
        var beforeUnlinked = await GetBodyAsync(client, unlinked);
        factory.Time.Advance(TimeSpan.FromMinutes(30));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/contacts/{deleted}", Ct)).StatusCode);

        using var fresh = factory.CreateClient();
        Assert.Equal(beforeOther, await GetBodyAsync(fresh, ofOther));
        Assert.Equal(beforeUnlinked, await GetBodyAsync(fresh, unlinked));
        var after = Members(await GetBodyAsync(fresh, ofDeleted));
        Assert.Equal(JsonValueKind.Null, after["contactId"].ValueKind);
    }

    [Fact]
    public async Task DeleteContactDirectlyInStore_ApiReturnsTodosUnlinked_AC066()
    {
        _ = factory.Server;
        using var client = factory.CreateClient();
        var contactId = await NewContactAsync(client, "ac066");
        var todoId = await CreateTodoAsync(client, "Store delete", contactId, "keep", "2026-12-24");
        await AssertLinkedAsync(client, factory.ConnectionString, todoId, contactId);
        var before = Members(await GetBodyAsync(client, todoId));

        await factory.ExecuteSqlAsync($"DELETE FROM Contacts WHERE lower(Id) = '{contactId.ToString().ToLowerInvariant()}'");

        using var fresh = factory.CreateClient();
        var after = Members(await GetBodyAsync(fresh, todoId));
        Assert.Equal(JsonValueKind.Null, after["contactId"].ValueKind);
        foreach (var (name, value) in before.Where(kv => kv.Key != "contactId"))
        {
            Assert.Equal(value.GetRawText(), after[name].GetRawText());
        }
    }
}
