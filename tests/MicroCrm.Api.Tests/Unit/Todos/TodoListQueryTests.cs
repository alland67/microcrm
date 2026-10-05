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
}
