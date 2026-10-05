using MicroCrm.Api.Features.Todos;

namespace MicroCrm.Api.Tests.Unit.Todos;

public sealed class TodoInputTests
{
    private static readonly CreateTodoRequest Valid = new("Call Ada", null, null, null);

    [Fact]
    public void Parse_TrimsTitleAndNotes_BlankNotesBecomeNull_AC004()
    {
        var (input, errors) = TodoInput.Parse(new CreateTodoRequest("  Call Ada\t", "\n Some notes  ", null, null));

        Assert.Null(errors);
        Assert.NotNull(input);
        Assert.Equal("Call Ada", input.Title);
        Assert.Equal("Some notes", input.Notes);

        foreach (var blank in new[] { "", "   ", "\t\n", null })
        {
            var (blankInput, blankErrors) = TodoInput.Parse(Valid with { Notes = blank });
            Assert.Null(blankErrors);
            Assert.NotNull(blankInput);
            Assert.Null(blankInput.Notes);
        }
    }

    [Theory]
    [InlineData("2026-10-05", 2026, 10, 5)]
    [InlineData(" 2026-10-05 ", 2026, 10, 5)]
    [InlineData("0001-01-01", 1, 1, 1)]
    [InlineData("9999-12-31", 9999, 12, 31)]
    [InlineData("2024-02-29", 2024, 2, 29)]
    public void Parse_ValidDueDate_ReturnsDateOnly_AC005(string value, int year, int month, int day)
    {
        var (input, errors) = TodoInput.Parse(Valid with { DueDate = value });

        Assert.Null(errors);
        Assert.NotNull(input);
        Assert.Equal(new DateOnly(year, month, day), input.DueDate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_BlankDueDate_ReturnsNull_AC006(string? value)
    {
        var (input, errors) = TodoInput.Parse(Valid with { DueDate = value });

        Assert.Null(errors);
        Assert.NotNull(input);
        Assert.Null(input.DueDate);
    }
}
