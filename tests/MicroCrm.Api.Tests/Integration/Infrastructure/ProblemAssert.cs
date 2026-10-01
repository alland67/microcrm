using System.Text.Json;

namespace MicroCrm.Api.Tests.Integration.Infrastructure;

// Shared assertions for RFC 9457 ProblemDetails responses (AC-037).
public static class ProblemAssert
{
    /// <summary>
    /// Asserts the status code, the application/problem+json content type, and the
    /// type/title/status members. When expectedErrorKeys is supplied, asserts the
    /// "errors" object contains exactly those keys.
    /// </summary>
    public static async Task<JsonDocument> IsProblemAsync(
        HttpResponseMessage response,
        int expectedStatus,
        params string[] expectedErrorKeys)
    {
        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var doc = JsonDocument.Parse(body);
        try
        {
            var root = doc.RootElement;

            Assert.Equal(JsonValueKind.Object, root.ValueKind);
            AssertNonEmptyString(root, "type");
            AssertNonEmptyString(root, "title");
            Assert.True(root.TryGetProperty("status", out var status), "ProblemDetails is missing 'status'");
            Assert.Equal(expectedStatus, status.GetInt32());

            if (expectedErrorKeys.Length > 0)
            {
                Assert.True(root.TryGetProperty("errors", out var errors), "ProblemDetails is missing 'errors'");
                var actual = errors.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
                Assert.Equal(expectedErrorKeys.Order(StringComparer.Ordinal).ToArray(), actual);
            }
        }
        catch
        {
            doc.Dispose();
            throw;
        }

        return doc;
    }

    private static void AssertNonEmptyString(JsonElement root, string name)
    {
        Assert.True(root.TryGetProperty(name, out var value), $"ProblemDetails is missing '{name}'");
        Assert.Equal(JsonValueKind.String, value.ValueKind);
        Assert.False(string.IsNullOrEmpty(value.GetString()), $"ProblemDetails '{name}' is empty");
    }
}
