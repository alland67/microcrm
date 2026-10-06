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

    public bool Complete(DateTimeOffset now)
    {
        if (IsDone)
        {
            return false;
        }

        IsDone = true;
        CompletedAt = now;
        UpdatedAt = now;
        return true;
    }

    public bool Reopen(DateTimeOffset now)
    {
        if (!IsDone)
        {
            return false;
        }

        IsDone = false;
        CompletedAt = null;
        UpdatedAt = now;
        return true;
    }
}
