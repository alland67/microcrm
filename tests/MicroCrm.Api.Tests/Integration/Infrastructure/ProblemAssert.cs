using System.Text;
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

    /// <summary>
    /// AC-039 / ADR-0006: asserts every message in "errors" starts with an uppercase letter, ends with a
    /// period, does not contain its key quoted ('key' or "key"), and does not start with the key or its
    /// humanized form (firstName / First name), ignoring case. A plain "contains the key" check is not used,
    /// because "Must be a valid email address." under "email" is compliant.
    /// </summary>
    public static void AssertValidationMessageStyle(JsonDocument problem)
    {
        Assert.True(problem.RootElement.TryGetProperty("errors", out var errors), "ProblemDetails is missing 'errors'");
        var checkedAny = false;
        foreach (var property in errors.EnumerateObject())
        {
            var key = property.Name;
            var humanized = Humanize(key);
            foreach (var element in property.Value.EnumerateArray())
            {
                checkedAny = true;
                var message = element.GetString();
                Assert.False(string.IsNullOrEmpty(message), $"'{key}' has an empty message");
                Assert.True(char.IsUpper(message[0]), $"'{key}' message must start with an uppercase letter: {message}");
                Assert.EndsWith(".", message, StringComparison.Ordinal);
                Assert.DoesNotContain($"'{key}'", message, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain($"\"{key}\"", message, StringComparison.OrdinalIgnoreCase);
                Assert.False(message.StartsWith(key, StringComparison.OrdinalIgnoreCase), $"'{key}' message must not start with the key: {message}");
                Assert.False(message.StartsWith(humanized, StringComparison.OrdinalIgnoreCase), $"'{key}' message must not start with '{humanized}': {message}");
            }
        }

        Assert.True(checkedAny, "errors contained no messages to check");
    }

    // "firstName" -> "First name"
    private static string Humanize(string key)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < key.Length; i++)
        {
            var c = key[i];
            if (i == 0)
            {
                sb.Append(char.ToUpperInvariant(c));
            }
            else if (char.IsUpper(c))
            {
                sb.Append(' ').Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static void AssertNonEmptyString(JsonElement root, string name)
    {
        Assert.True(root.TryGetProperty(name, out var value), $"ProblemDetails is missing '{name}'");
        Assert.Equal(JsonValueKind.String, value.ValueKind);
        Assert.False(string.IsNullOrEmpty(value.GetString()), $"ProblemDetails '{name}' is empty");
    }
}
