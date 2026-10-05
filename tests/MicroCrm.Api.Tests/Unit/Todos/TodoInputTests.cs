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

    private const string DateMessage = "Must be a valid date in YYYY-MM-DD format.";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Parse_MissingTitle_ReturnsRequired_AC010(string? title)
    {
        var (input, errors) = TodoInput.Parse(Valid with { Title = title });

        Assert.Null(input);
        Assert.NotNull(errors);
        var entry = Assert.Single(errors);
        Assert.Equal("title", entry.Key);
        Assert.Equal(["Required."], entry.Value);
    }

    [Fact]
    public void Parse_OverMax_ReturnsMaxMessage_AC011()
    {
        var (input, errors) = TodoInput.Parse(Valid with { Title = new string('a', 201), Notes = new string('b', 4001) });

        Assert.Null(input);
        Assert.NotNull(errors);
        Assert.Equal(2, errors.Count);
        Assert.Equal(["Must be 200 characters or fewer."], errors["title"]);
        Assert.Equal(["Must be 4000 characters or fewer."], errors["notes"]);
    }

    [Fact]
    public void Parse_AtMax_AfterTrimming_IsAccepted_AC011()
    {
        var (input, errors) = TodoInput.Parse(Valid with
        {
            Title = " " + new string('a', 200) + " ",
            Notes = " " + new string('b', 4000) + " ",
        });

        Assert.Null(errors);
        Assert.NotNull(input);
    }

    [Theory]
    [InlineData("2026-13-01")]
    [InlineData("2026-02-30")]
    [InlineData("05/10/2026")]
    [InlineData("2026-10-5")]
    [InlineData("2026-10-05T00:00:00Z")]
    [InlineData("226-10-05")]
    [InlineData("02026-10-05")]
    [InlineData("２０２６-10-05")]
    [InlineData("2026/10/05")]
    public void Parse_InvalidDueDate_ReturnsDateMessage_AC012(string value)
    {
        var (input, errors) = TodoInput.Parse(Valid with { DueDate = value });

        Assert.Null(input);
        Assert.NotNull(errors);
        var entry = Assert.Single(errors);
        Assert.Equal("dueDate", entry.Key);
        Assert.Equal([DateMessage], entry.Value);
    }

    [Fact]
    public void Parse_MultipleErrors_ReturnsAllKeys_AC014()
    {
        var (input, errors) = TodoInput.Parse(new CreateTodoRequest("  ", new string('b', 4001), "nope", null));

        Assert.Null(input);
        Assert.NotNull(errors);
        Assert.Equal(["dueDate", "notes", "title"], errors.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(["Required."], errors["title"]);
        Assert.Equal(["Must be 4000 characters or fewer."], errors["notes"]);
        Assert.Equal([DateMessage], errors["dueDate"]);
    }
}
