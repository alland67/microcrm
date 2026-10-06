using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

namespace MicroCrm.Api.Tests.Integration.Todos;

public sealed class GetTodoByIdTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetTodo_Existing_Returns200WithShape_AC018()
    {
        using var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/todos", new { title = "Fetch me", notes = "n", dueDate = "2026-10-05" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdBody = await created.Content.ReadAsStringAsync(Ct);
        using var createdDoc = JsonDocument.Parse(createdBody);
        var id = createdDoc.RootElement.GetProperty("id").GetGuid();

        var response = await client.GetAsync($"/api/todos/{id}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(Ct);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal(id, root.GetProperty("id").GetGuid());
        Assert.Equal("Fetch me", root.GetProperty("title").GetString());
        Assert.Equal("n", root.GetProperty("notes").GetString());
        Assert.Equal("2026-10-05", root.GetProperty("dueDate").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("contactId").ValueKind);
        Assert.False(root.GetProperty("isDone").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("completedAt").ValueKind);
        Assert.Equal(createdBody, body);
    }

    // Guard at RED: with no route yet, status code pages already produce a 404 problem+json.
    [Fact]
    public async Task GetTodo_UnknownGuid_Returns404Problem_AC019()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/todos/{Guid.CreateVersion7()}", Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 404);
    }

    // Guard at RED (see above).
    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("123")]
    public async Task GetTodo_NonGuidId_Returns404Problem_AC019(string id)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/todos/{id}", Ct);

        using var problem = await ProblemAssert.IsProblemAsync(response, 404);
    }
}
