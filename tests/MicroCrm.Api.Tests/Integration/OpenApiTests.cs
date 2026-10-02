using System.Text.Json;

using MicroCrm.Api.Tests.Integration.Infrastructure;

namespace MicroCrm.Api.Tests.Integration;

public sealed class OpenApiTests(ApiFactory factory)
    : IClassFixture<ApiFactory>
{
    private async Task<JsonElement> GetDocumentAsync()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode, $"Expected 2xx, got {(int)response.StatusCode}");
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static JsonElement GetOperation(JsonElement doc, string path, string method)
    {
        Assert.True(doc.TryGetProperty("paths", out var paths), "OpenAPI document has no 'paths'");
        Assert.True(paths.TryGetProperty(path, out var item), $"OpenAPI document has no path '{path}'");
        Assert.True(
            item.TryGetProperty(method, out var operation),
            $"OpenAPI path '{path}' has no '{method}' operation");
        return operation;
    }

    private static string? OperationId(JsonElement op) =>
        op.TryGetProperty("operationId", out var v) ? v.GetString() : null;

    private static string? Summary(JsonElement op) =>
        op.TryGetProperty("summary", out var v) ? v.GetString() : null;

    private static string[] ResponseCodes(JsonElement op) =>
        op.TryGetProperty("responses", out var r)
            ? [.. r.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)]
            : [];

    private static void AssertCodes(JsonElement op, string label, params string[] required)
    {
        var codes = ResponseCodes(op);
        var missing = required.Where(c => !codes.Contains(c)).ToArray();
        Assert.True(
            missing.Length == 0,
            $"{label} is missing response codes [{string.Join(", ", missing)}]; documented: [{string.Join(", ", codes)}]");
    }

    [Fact]
    public async Task OpenApi_ContactsEndpoints_HaveOperationIds_NFR001()
    {
        var doc = await GetDocumentAsync();

        var create = GetOperation(doc, "/api/contacts", "post");
        var list = GetOperation(doc, "/api/contacts", "get");
        var getById = GetOperation(doc, "/api/contacts/{id}", "get");

        // Operation ids
        Assert.Equal("CreateContact", OperationId(create));
        Assert.Equal("GetContactById", OperationId(getById));
        Assert.Equal("ListContacts", OperationId(list));
    }

    [Fact]
    public async Task OpenApi_ContactsEndpoints_HaveNonEmptySummaries_NFR001()
    {
        var doc = await GetDocumentAsync();

        Assert.False(
            string.IsNullOrWhiteSpace(Summary(GetOperation(doc, "/api/contacts", "post"))),
            "POST /api/contacts has no summary");
        Assert.False(
            string.IsNullOrWhiteSpace(Summary(GetOperation(doc, "/api/contacts/{id}", "get"))),
            "GET /api/contacts/{id} has no summary");
        Assert.False(
            string.IsNullOrWhiteSpace(Summary(GetOperation(doc, "/api/contacts", "get"))),
            "GET /api/contacts has no summary");
    }

    [Fact]
    public async Task OpenApi_CreateContact_DocumentsStatusCodes201_400_409_NFR001()
    {
        var doc = await GetDocumentAsync();

        AssertCodes(GetOperation(doc, "/api/contacts", "post"), "POST /api/contacts", "201", "400", "409");
    }

    [Fact]
    public async Task OpenApi_GetContactById_DocumentsStatusCodes200_404_NFR001()
    {
        var doc = await GetDocumentAsync();

        AssertCodes(GetOperation(doc, "/api/contacts/{id}", "get"), "GET /api/contacts/{id}", "200", "404");
    }

    [Fact]
    public async Task OpenApi_ListContacts_DocumentsStatusCodes200_400_NFR001()
    {
        var doc = await GetDocumentAsync();

        AssertCodes(GetOperation(doc, "/api/contacts", "get"), "GET /api/contacts", "200", "400");
    }
}
