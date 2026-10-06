using MicroCrm.Api.Common;
using MicroCrm.Api.Features.Todos;

namespace MicroCrm.Api.Tests.Unit.Todos;

public sealed class TodoListQueryTests
{
    [Fact]
    public void Parse_Defaults_Page1PageSize20NoFilters_AC042()
    {
        var (query, errors) = TodoListQuery.Parse(null, null, null, null);

        Assert.Null(errors);
        Assert.NotNull(query);
        Assert.Equal(1, query.Paging.Page);
        Assert.Equal(20, query.Paging.PageSize);
        Assert.Null(query.Status);
        Assert.Null(query.ContactId);
    }

    [Theory]
    [InlineData("page", "0")]
    [InlineData("page", "-1")]
    [InlineData("page", "abc")]
    [InlineData("page", "1.5")]
    [InlineData("page", " 1")]
    [InlineData("pageSize", "0")]
    [InlineData("pageSize", "-1")]
    [InlineData("pageSize", "101")]
    [InlineData("pageSize", "abc")]
    [InlineData("pageSize", "1.5")]
    [InlineData("pageSize", " 1")]
    public void Parse_InvalidPagingValues_ReturnSameMessagesAsContacts_AC046(string parameter, string value)
    {
        var page = parameter == "page" ? value : null;
        var pageSize = parameter == "pageSize" ? value : null;
        var (_, contactErrors) = ListQuery.Parse(page, pageSize, null);
        Assert.NotNull(contactErrors);

        var (query, errors) = TodoListQuery.Parse(page, pageSize, null, null);

        Assert.Null(query);
        Assert.NotNull(errors);
        Assert.Equal([parameter], errors.Keys.ToArray());
        Assert.Equal(contactErrors[parameter], errors[parameter]);
        var expected = parameter == "page"
            ? "Must be an integer between 1 and 2147483647."
            : "Must be an integer between 1 and 100.";
        Assert.Equal([expected], errors[parameter]);
    }

    [Theory]
    [InlineData("open", TodoStatus.Open)]
    [InlineData("done", TodoStatus.Done)]
    [InlineData("overdue", TodoStatus.Overdue)]
    [InlineData("OPEN", TodoStatus.Open)]
    [InlineData(" Done ", TodoStatus.Done)]
    [InlineData("OverDue", TodoStatus.Overdue)]
    [InlineData("\tOVERDUE\n", TodoStatus.Overdue)]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void Parse_Status_TrimmedCaseInsensitiveBlankMeansNone_AC051(string? value, TodoStatus? expected)
    {
        var (query, errors) = TodoListQuery.Parse(null, null, value, null);

        Assert.Null(errors);
        Assert.NotNull(query);
        Assert.Equal(expected, query.Status);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("opened")]
    [InlineData("1")]
    [InlineData("open done")]
    [InlineData("open,done")]
    [InlineData("open, overdue")]
    [InlineData("done,done")]
    public void Parse_UnknownStatus_ReturnsOneOfMessage_AC052(string value)
    {
        var (query, errors) = TodoListQuery.Parse(null, null, value, null);

        Assert.Null(query);
        Assert.NotNull(errors);
        Assert.Equal(["status"], errors.Keys.ToArray());
        Assert.Equal(["Must be one of: open, done, overdue."], errors["status"]);
    }

    [Fact]
    public void Parse_ContactId_GuidOrBlank_AC053()
    {
        var id = Guid.CreateVersion7();

        var (plain, plainErrors) = TodoListQuery.Parse(null, null, null, id.ToString());
        var (padded, paddedErrors) = TodoListQuery.Parse(null, null, null, $"  {id.ToString().ToUpperInvariant()} ");
        var (empty, emptyErrors) = TodoListQuery.Parse(null, null, null, "");
        var (blank, blankErrors) = TodoListQuery.Parse(null, null, null, "   ");

        Assert.Null(plainErrors);
        Assert.Null(paddedErrors);
        Assert.Null(emptyErrors);
        Assert.Null(blankErrors);
        Assert.Equal(id, plain!.ContactId);
        Assert.Equal(id, padded!.ContactId);
        Assert.Null(empty!.ContactId);
        Assert.Null(blank!.ContactId);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("123")]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950")]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950ez")]
    public void Parse_MalformedContactId_ReturnsGuidMessage_AC054(string value)
    {
        var (query, errors) = TodoListQuery.Parse(null, null, null, value);

        Assert.Null(query);
        Assert.NotNull(errors);
        Assert.Equal(["contactId"], errors.Keys.ToArray());
        Assert.Equal(["Must be a valid GUID."], errors["contactId"]);
    }

    [Fact]
    public void Parse_AllInvalid_ReportsEveryParameter_AC056()
    {
        var (query, errors) = TodoListQuery.Parse("0", "101", "pending", "nope");

        Assert.Null(query);
        Assert.NotNull(errors);
        Assert.Equal(
            new[] { "contactId", "page", "pageSize", "status" },
            errors.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(["Must be an integer between 1 and 2147483647."], errors["page"]);
        Assert.Equal(["Must be an integer between 1 and 100."], errors["pageSize"]);
        Assert.Equal(["Must be one of: open, done, overdue."], errors["status"]);
        Assert.Equal(["Must be a valid GUID."], errors["contactId"]);
    }
}
