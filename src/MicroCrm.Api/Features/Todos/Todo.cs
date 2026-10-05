namespace MicroCrm.Api.Features.Todos;

public sealed class Todo
{
    public Guid Id { get; set; }

    public string Title { get; set; } = "";

    public string? Notes { get; set; }

    public DateOnly? DueDate { get; set; }

    public Guid? ContactId { get; set; }

    public bool IsDone { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
