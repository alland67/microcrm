using MicroCrm.Api.Common;

namespace MicroCrm.Api.Features.Todos;

public enum TodoStatus { Open, Done, Overdue }

public sealed record TodoListQuery(ListQuery Paging, TodoStatus? Status, Guid? ContactId)
{
    public static (TodoListQuery? Query, Dictionary<string, string[]>? Errors) Parse(
        string? page, string? pageSize, string? status, string? contactId)
    {
        var (paging, errors) = ListQuery.Parse(page, pageSize, search: null);

        return paging is null
            ? (null, errors)
            : (new TodoListQuery(paging, Status: null, ContactId: null), null);
    }
}
