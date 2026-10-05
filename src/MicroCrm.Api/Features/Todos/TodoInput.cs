using System.Globalization;

using static MicroCrm.Api.Common.TextNormalization;

namespace MicroCrm.Api.Features.Todos;

public sealed record TodoInput(string Title, string? Notes, DateOnly? DueDate)
{
    public const int TitleMax = 200, NotesMax = 4000;

    private const string DateFormat = "yyyy-MM-dd";

    public static (TodoInput? Input, Dictionary<string, string[]>? Errors) Parse(CreateTodoRequest request) =>
        Parse(request.Title, request.Notes, request.DueDate);

    private static (TodoInput? Input, Dictionary<string, string[]>? Errors) Parse(
        string? rawTitle, string? rawNotes, string? rawDueDate)
    {
        var errors = new Dictionary<string, string[]>();

        var title = rawTitle?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            errors["title"] = ["Required."];
        }
        else
        {
            CheckMax(errors, "title", title, TitleMax);
        }

        var notes = TrimToNull(rawNotes);
        CheckMax(errors, "notes", notes, NotesMax);

        DateOnly? dueDate = null;
        var rawDate = TrimToNull(rawDueDate);
        if (rawDate is not null)
        {
            if (TryParseDate(rawDate, out var parsed))
            {
                dueDate = parsed;
            }
            else
            {
                errors["dueDate"] = ["Must be a valid date in YYYY-MM-DD format."];
            }
        }

        return errors.Count > 0
            ? (null, errors)
            : (new TodoInput(title!, notes, dueDate), null);
    }

    private static bool TryParseDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static void CheckMax(Dictionary<string, string[]> errors, string field, string? value, int max)
    {
        if (value is not null && value.Length > max)
        {
            errors[field] = [$"Must be {max} characters or fewer."];
        }
    }
}
