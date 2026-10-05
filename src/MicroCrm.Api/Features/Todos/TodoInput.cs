using System.Globalization;

namespace MicroCrm.Api.Features.Todos;

public sealed record TodoInput(string Title, string? Notes, DateOnly? DueDate)
{
    public static (TodoInput? Input, Dictionary<string, string[]>? Errors) Parse(CreateTodoRequest request) =>
        Parse(request.Title, request.Notes, request.DueDate);

    private static (TodoInput? Input, Dictionary<string, string[]>? Errors) Parse(
        string? rawTitle, string? rawNotes, string? rawDueDate)
    {
        var dueDate = TrimToNull(rawDueDate);
        DateOnly? parsedDueDate = dueDate is not null
            && DateOnly.TryParseExact(dueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d
                : null;

        return (new TodoInput(rawTitle?.Trim() ?? string.Empty, TrimToNull(rawNotes), parsedDueDate), null);
    }

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
