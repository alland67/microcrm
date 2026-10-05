using MicroCrm.Api.Common;

namespace MicroCrm.Api.Features.Todos;

public enum TodoStatus { Open, Done, Overdue }

public sealed record TodoListQuery(ListQuery Paging, TodoStatus? Status, Guid? ContactId)
{
    public static (TodoListQuery? Query, Dictionary<string, string[]>? Errors) Parse(
        string? page, string? pageSize, string? status, string? contactId)
    {
        var (paging, errors) = ListQuery.Parse(page, pageSize, search: null);
        errors ??= [];

        TodoStatus? parsedStatus = null;
        var trimmedStatus = status?.Trim();
        if (!string.IsNullOrEmpty(trimmedStatus))
        {
            if (TryParseStatus(trimmedStatus, out var value))
            {
                parsedStatus = value;
            }
            else
            {
                errors["status"] = ["Must be one of: open, done, overdue."];
            }
        }

        Guid? parsedContactId = null;
        var trimmedContactId = contactId?.Trim();
        if (!string.IsNullOrEmpty(trimmedContactId))
        {
            if (Guid.TryParse(trimmedContactId, out var guid))
            {
                parsedContactId = guid;
            }
            else
            {
                errors["contactId"] = ["Must be a valid GUID."];
            }
        }

        return paging is null || errors.Count > 0
            ? (null, errors)
            : (new TodoListQuery(paging, parsedStatus, parsedContactId), null);
    }

    private static bool TryParseStatus(string text, out TodoStatus status)
    {
        TodoStatus? match =
            string.Equals(text, "open", StringComparison.OrdinalIgnoreCase) ? TodoStatus.Open :
            string.Equals(text, "done", StringComparison.OrdinalIgnoreCase) ? TodoStatus.Done :
            string.Equals(text, "overdue", StringComparison.OrdinalIgnoreCase) ? TodoStatus.Overdue :
            null;
        status = match.GetValueOrDefault();
        return match.HasValue;
    }
}
