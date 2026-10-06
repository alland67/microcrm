namespace MicroCrm.Api.Features.Todos;

public sealed record CreateTodoRequest(string? Title, string? Notes, string? DueDate, string? ContactId);

public sealed record UpdateTodoRequest(string? Title, string? Notes, string? DueDate, string? ContactId);

public sealed record TodoResponse(
    Guid Id,
    string Title,
    string? Notes,
    Guid? ContactId,
    DateOnly? DueDate,
    bool IsDone,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static TodoResponse From(Todo todo) => new(
        todo.Id,
        todo.Title,
        todo.Notes,
        todo.ContactId,
        todo.DueDate,
        todo.IsDone,
        todo.CompletedAt,
        todo.CreatedAt,
        todo.UpdatedAt);
}
