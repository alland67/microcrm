namespace MicroCrm.Api.Common;

public static class TextNormalization
{
    public static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
