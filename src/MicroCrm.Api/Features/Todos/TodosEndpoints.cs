using MicroCrm.Api.Common;
using MicroCrm.Api.Data;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MicroCrm.Api.Features.Todos;

public static class TodosEndpoints
{
    public static IEndpointRouteBuilder MapTodosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/todos");

        group.MapPost(string.Empty, CreateTodo);
        group.MapGet(string.Empty, ListTodos);
        group.MapGet("/{id:guid}", GetTodoById);
        group.MapPut("/{id:guid}", UpdateTodo);
        group.MapPost("/{id:guid}/complete", CompleteTodo);
        group.MapPost("/{id:guid}/reopen", ReopenTodo);
        group.MapDelete("/{id:guid}", DeleteTodo);

        return app;
    }

    private static async Task<Results<Created<TodoResponse>, ValidationProblem>> CreateTodo(
        CreateTodoRequest request,
        AppDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var (input, errors) = TodoInput.Parse(request);
        if (input is null)
        {
            return TypedResults.ValidationProblem(errors!);
        }

        var now = time.GetUtcNow();
        var todo = new Todo
        {
            Id = Guid.CreateVersion7(now),
            Title = input.Title,
            Notes = input.Notes,
            DueDate = input.DueDate,
            ContactId = input.ContactId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Todos.Add(todo);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsForeignKeyViolation(ex))
        {
            return UnknownContact();
        }

        return TypedResults.Created($"/api/todos/{todo.Id}", TodoResponse.From(todo));
    }

    private static async Task<Results<Ok<PagedResponse<TodoResponse>>, ValidationProblem>> ListTodos(
        AppDbContext db,
        TimeProvider time,
        CancellationToken ct,
        string? page = null,
        string? pageSize = null,
        string? status = null,
        string? contactId = null)
    {
        var (query, errors) = TodoListQuery.Parse(page, pageSize, status, contactId);
        if (query is null)
        {
            return TypedResults.ValidationProblem(errors!);
        }

        var todos = ApplyFilters(db.Todos.AsNoTracking(), query, time);
        var totalCount = await todos.CountAsync(ct);

        var items = new List<TodoResponse>();
        if (query.Paging.TryGetSkip(out var skip))
        {
            var rows = await todos
                .OrderBy(t => t.DueDate == null)
                .ThenBy(t => t.DueDate)
                .ThenBy(t => t.CreatedAt)
                .ThenBy(t => t.Id)
                .Skip(skip)
                .Take(query.Paging.PageSize)
                .ToListAsync(ct);
            items.AddRange(rows.Select(TodoResponse.From));
        }

        return TypedResults.Ok(new PagedResponse<TodoResponse>(
            items, query.Paging.Page, query.Paging.PageSize, totalCount));
    }

    private static IQueryable<Todo> ApplyFilters(IQueryable<Todo> todos, TodoListQuery query, TimeProvider time)
    {
        if (query.ContactId is { } contactId)
        {
            todos = todos.Where(t => t.ContactId == contactId);
        }

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        return query.Status switch
        {
            TodoStatus.Open => todos.Where(t => !t.IsDone),
            TodoStatus.Done => todos.Where(t => t.IsDone),
            TodoStatus.Overdue => todos.Where(t => !t.IsDone && t.DueDate != null && t.DueDate < today),
            _ => todos,
        };
    }

    private static async Task<Results<Ok<TodoResponse>, ProblemHttpResult>> GetTodoById(
        Guid id,
        AppDbContext db,
        CancellationToken ct)
    {
        var todo = await db.Todos.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);

        return todo is null
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound)
            : TypedResults.Ok(TodoResponse.From(todo));
    }

    private static async Task<Results<Ok<TodoResponse>, ProblemHttpResult, ValidationProblem>> UpdateTodo(
        Guid id,
        UpdateTodoRequest request,
        AppDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var (input, errors) = TodoInput.Parse(request);
        if (input is null)
        {
            return TypedResults.ValidationProblem(errors!);
        }

        var todo = await db.Todos.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (todo is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound);
        }

        todo.Title = input.Title;
        todo.Notes = input.Notes;
        todo.DueDate = input.DueDate;
        todo.ContactId = input.ContactId;
        todo.UpdatedAt = time.GetUtcNow();

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsForeignKeyViolation(ex))
        {
            return UnknownContact();
        }

        return TypedResults.Ok(TodoResponse.From(todo));
    }

    private static Task<Results<Ok<TodoResponse>, ProblemHttpResult>> CompleteTodo(
        Guid id,
        AppDbContext db,
        TimeProvider time,
        CancellationToken ct) =>
        ChangeDoneState(id, db, (todo, now) => todo.Complete(now), time, ct);

    private static Task<Results<Ok<TodoResponse>, ProblemHttpResult>> ReopenTodo(
        Guid id,
        AppDbContext db,
        TimeProvider time,
        CancellationToken ct) =>
        ChangeDoneState(id, db, (todo, now) => todo.Reopen(now), time, ct);

    private static async Task<Results<Ok<TodoResponse>, ProblemHttpResult>> ChangeDoneState(
        Guid id,
        AppDbContext db,
        Func<Todo, DateTimeOffset, bool> change,
        TimeProvider time,
        CancellationToken ct)
    {
        var todo = await db.Todos.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (todo is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound);
        }

        if (change(todo, time.GetUtcNow()))
        {
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(TodoResponse.From(todo));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteTodo(
        Guid id,
        AppDbContext db,
        CancellationToken ct)
    {
        var deleted = await db.Todos.Where(t => t.Id == id).ExecuteDeleteAsync(ct);

        return deleted == 0
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound)
            : TypedResults.NoContent();
    }

    private static ValidationProblem UnknownContact() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            ["contactId"] = ["Must refer to an existing contact."],
        });
}
