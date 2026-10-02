namespace MicroCrm.Api.Common;

public static class LikePattern
{
    public const string EscapeChar = "\\";

    // "50%_a\b" -> "%50\%\_a\\b%": escape '\' first, then '%' and '_', then wrap for a "contains" match.
    public static string Contains(string term)
    {
        var escaped = term
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }
}
