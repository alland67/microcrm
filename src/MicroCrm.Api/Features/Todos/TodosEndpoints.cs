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
        group.MapGet("/{id:guid}", GetTodoById);

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
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["contactId"] = ["Must refer to an existing contact."],
            });
        }

        return TypedResults.Created($"/api/todos/{todo.Id}", TodoResponse.From(todo));
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
}
