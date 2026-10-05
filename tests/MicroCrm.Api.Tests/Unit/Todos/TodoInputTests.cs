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

    private const string GuidMessage = "Must be a valid GUID.";

    [Theory]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e", "0f8fad5b-d9cb-469f-a165-70867728950e")]
    [InlineData("  0F8FAD5B-D9CB-469F-A165-70867728950E\t", "0f8fad5b-d9cb-469f-a165-70867728950e")]
    [InlineData("0f8fad5bd9cb469fa16570867728950e", "0f8fad5b-d9cb-469f-a165-70867728950e")]
    [InlineData("{0f8fad5b-d9cb-469f-a165-70867728950e}", "0f8fad5b-d9cb-469f-a165-70867728950e")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("\t\n", null)]
    public void Parse_ContactId_ParsesGuidOrBlankToNull_AC008(string? value, string? expected)
    {
        var (input, errors) = TodoInput.Parse(Valid with { ContactId = value });

        Assert.Null(errors);
        Assert.NotNull(input);
        Assert.Equal(expected is null ? null : Guid.Parse(expected), input.ContactId);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("123")]
    [InlineData("0f8fad5b-d9cb-469f-a165")]
    public void Parse_MalformedContactId_ReturnsGuidMessage_AC013(string value)
    {
        var (input, errors) = TodoInput.Parse(Valid with { ContactId = value });

        Assert.Null(input);
        Assert.NotNull(errors);
        var entry = Assert.Single(errors);
        Assert.Equal("contactId", entry.Key);
        Assert.Equal([GuidMessage], entry.Value);
    }

    // contactId must not short-circuit the other rules, nor be skipped when another rule fails first.
    [Theory]
    [InlineData("title")]
    [InlineData("notes")]
    [InlineData("dueDate")]
    public void Parse_ContactIdWithOneOtherInvalidField_ReportsBoth_AC014(string other)
    {
        var request = Valid with { ContactId = "abc" };
        request = other switch
        {
            "title" => request with { Title = "  " },
            "notes" => request with { Notes = new string('b', 4001) },
            _ => request with { DueDate = "nope" },
        };

        var (input, errors) = TodoInput.Parse(request);

        Assert.Null(input);
        Assert.NotNull(errors);
        Assert.Equal(
            new[] { other, "contactId" }.Order(StringComparer.Ordinal).ToArray(),
            errors.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal([GuidMessage], errors["contactId"]);
    }

    [Fact]
    public void Parse_AllFourFieldsInvalid_ReturnsAllFourKeys_AC014()
    {
        var (input, errors) = TodoInput.Parse(new CreateTodoRequest("  ", new string('b', 4001), "nope", "abc"));

        Assert.Null(input);
        Assert.NotNull(errors);
        Assert.Equal(["contactId", "dueDate", "notes", "title"], errors.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(["Required."], errors["title"]);
        Assert.Equal(["Must be 4000 characters or fewer."], errors["notes"]);
        Assert.Equal([DateMessage], errors["dueDate"]);
        Assert.Equal([GuidMessage], errors["contactId"]);
    }

    public static TheoryData<string?, string?, string?, string?> InvalidPayloads => new()
    {
        { null, null, null, null },
        { "  ", null, null, null },
        { "Fine", new string('b', 4001), null, null },
        { new string('a', 201), null, "2026-02-30", null },
        { "Fine", null, "05/10/2026", "abc" },
        { "  ", new string('b', 4001), "nope", "abc" },
    };

    // Guard: create and update share one parse core.
    [Theory]
    [MemberData(nameof(InvalidPayloads))]
    public void Parse_UpdateAndCreateRequests_ProduceIdenticalErrors_NFR005(
        string? title, string? notes, string? dueDate, string? contactId)
    {
        var (createInput, createErrors) = TodoInput.Parse(new CreateTodoRequest(title, notes, dueDate, contactId));
        var (updateInput, updateErrors) = TodoInput.Parse(new UpdateTodoRequest(title, notes, dueDate, contactId));

        Assert.Null(createInput);
        Assert.Null(updateInput);
        Assert.NotNull(createErrors);
        Assert.NotNull(updateErrors);
        Assert.Equal(
            createErrors.Keys.Order(StringComparer.Ordinal).ToArray(),
            updateErrors.Keys.Order(StringComparer.Ordinal).ToArray());
        foreach (var (key, messages) in createErrors)
        {
            Assert.Equal(messages, updateErrors[key]);
        }
    }
}
