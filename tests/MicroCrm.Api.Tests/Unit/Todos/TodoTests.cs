using MicroCrm.Api.Features.Todos;

namespace MicroCrm.Api.Tests.Unit.Todos;

public sealed class TodoTests
{
    private static readonly DateTimeOffset Created = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = Created.AddHours(2);

    private static Todo Open() => new()
    {
        Id = Guid.CreateVersion7(),
        Title = "Call Ada",
        CreatedAt = Created,
        UpdatedAt = Created,
    };

    private static Todo Done()
    {
        var todo = Open();
        todo.IsDone = true;
        todo.CompletedAt = Created.AddHours(1);
        todo.UpdatedAt = Created.AddHours(1);
        return todo;
    }

    [Fact]
    public void Complete_Open_SetsDoneAndTimestamps_ReturnsTrue_AC031()
    {
        var todo = Open();

        var changed = todo.Complete(Now);

        Assert.True(changed);
        Assert.True(todo.IsDone);
        Assert.Equal(Now, todo.CompletedAt);
        Assert.Equal(Now, todo.UpdatedAt);
        Assert.Equal(Created, todo.CreatedAt);
        Assert.Equal("Call Ada", todo.Title);
    }

    [Fact]
    public void Complete_AlreadyDone_ChangesNothing_ReturnsFalse_AC032()
    {
        var todo = Done();
        var completedAt = todo.CompletedAt;
        var updatedAt = todo.UpdatedAt;

        var changed = todo.Complete(Now);

        Assert.False(changed);
        Assert.True(todo.IsDone);
        Assert.Equal(completedAt, todo.CompletedAt);
        Assert.Equal(updatedAt, todo.UpdatedAt);
    }

    [Fact]
    public void Reopen_Done_ClearsCompletedAtAndSetsUpdatedAt_ReturnsTrue_AC033()
    {
        var todo = Done();

        var changed = todo.Reopen(Now);

        Assert.True(changed);
        Assert.False(todo.IsDone);
        Assert.Null(todo.CompletedAt);
        Assert.Equal(Now, todo.UpdatedAt);
        Assert.Equal(Created, todo.CreatedAt);
    }

    [Fact]
    public void Reopen_AlreadyOpen_ChangesNothing_ReturnsFalse_AC034()
    {
        var todo = Open();

        var changed = todo.Reopen(Now);

        Assert.False(changed);
        Assert.False(todo.IsDone);
        Assert.Null(todo.CompletedAt);
        Assert.Equal(Created, todo.UpdatedAt);
    }
}
