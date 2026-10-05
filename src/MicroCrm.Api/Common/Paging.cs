using System.Globalization;

namespace MicroCrm.Api.Common;

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record ListQuery(int Page, int PageSize, string? Search)
{
    public const int DefaultPage = 1, DefaultPageSize = 20, MaxPageSize = 100, MaxSearchLength = 254;

    // A null value (parameter absent) uses the default; a present value must be a plain
    // non-negative integer (no sign or whitespace) within range; otherwise an error is reported under that key.
    public static (ListQuery? Query, Dictionary<string, string[]>? Errors) Parse(
        string? page, string? pageSize, string? search)
    {
        var errors = new Dictionary<string, string[]>();
        var parsedPage = ParseValue(page, "page", DefaultPage, 1, int.MaxValue, errors);
        var parsedPageSize = ParseValue(pageSize, "pageSize", DefaultPageSize, 1, MaxPageSize, errors);

        // A blank search is the same as no search (unlike a blank page/pageSize).
        var trimmed = search?.Trim();
        var term = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        if (term is { Length: > MaxSearchLength })
        {
            errors["search"] = [$"Must be {MaxSearchLength} characters or fewer."];
        }

        return errors.Count > 0
            ? (null, errors)
            : (new ListQuery(parsedPage, parsedPageSize, term), null);
    }

    // Rows to skip, computed in long. Returns false (=> empty page) when it exceeds int.MaxValue.
    public bool TryGetSkip(out int skip)
    {
        var rows = ((long)Page - 1) * PageSize;
        skip = rows > int.MaxValue ? 0 : (int)rows;
        return rows <= int.MaxValue;
    }

    private static int ParseValue(
        string? value, string key, int fallback, int min, int max, Dictionary<string, string[]> errors)
    {
        if (value is null)
        {
            return fallback;
        }

        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            && parsed >= min && parsed <= max)
        {
            return parsed;
        }

        errors[key] = [$"Must be an integer between {min} and {max}."];
        return fallback;
    }
}
