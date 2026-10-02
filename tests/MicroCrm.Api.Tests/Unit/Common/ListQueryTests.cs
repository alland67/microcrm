using MicroCrm.Api.Common;

namespace MicroCrm.Api.Tests.Unit.Common;

public sealed class ListQueryTests
{
    // " 1" and "+1" kill the mutant that replaces NumberStyles.None with the default int.TryParse;
    // "-1" and "0" kill the removed-min-check mutant; "101" kills the removed-max-check mutant.
    [Theory]
    [InlineData("0", null, "page")]
    [InlineData("-1", null, "page")]
    [InlineData("abc", null, "page")]
    [InlineData("1.5", null, "page")]
    [InlineData("", null, "page")]
    [InlineData("2147483648", null, "page")]
    [InlineData(" 1", null, "page")]
    [InlineData("+1", null, "page")]
    [InlineData(null, "0", "pageSize")]
    [InlineData(null, "101", "pageSize")]
    [InlineData(null, "-1", "pageSize")]
    [InlineData(null, "abc", "pageSize")]
    [InlineData(null, " 1", "pageSize")]
    [InlineData(null, "+1", "pageSize")]
    [InlineData(null, "", "pageSize")]
    [InlineData("  ", null, "page")]
    public void Parse_InvalidPageOrPageSize_ReturnsNamedError_AC028(string? page, string? pageSize, string expectedKey)
    {
        var (query, errors) = ListQuery.Parse(page, pageSize, null);

        Assert.Null(query);
        Assert.NotNull(errors);
        Assert.Equal(new[] { expectedKey }, errors.Keys.ToArray());
        Assert.NotEmpty(errors[expectedKey]);
    }

    [Fact]
    public void Parse_BothInvalid_ReportsBothKeys_AC028()
    {
        var (query, errors) = ListQuery.Parse("0", "101", null);

        Assert.Null(query);
        Assert.NotNull(errors);
        Assert.Equal(new[] { "page", "pageSize" }, errors.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Parse_Absent_UsesDefaults_AC028()
    {
        var (query, errors) = ListQuery.Parse(null, null, null);

        Assert.Null(errors);
        Assert.NotNull(query);
        Assert.Equal(1, query.Page);
        Assert.Equal(20, query.PageSize);
    }

    [Theory]
    [InlineData("1", "1", 1, 1)]
    [InlineData(null, "1", 1, 1)]
    [InlineData(null, "100", 1, 100)]
    [InlineData("2147483647", null, int.MaxValue, 20)]
    [InlineData("2", "50", 2, 50)]
    public void Parse_ValidBoundaries_AreAccepted_AC028(string? page, string? pageSize, int expectedPage, int expectedPageSize)
    {
        var (query, errors) = ListQuery.Parse(page, pageSize, null);

        Assert.Null(errors);
        Assert.NotNull(query);
        Assert.Equal(expectedPage, query.Page);
        Assert.Equal(expectedPageSize, query.PageSize);
    }

    [Fact]
    public void Parse_SearchOver254AfterTrim_ReturnsSearchError_AC034()
    {
        var (query, errors) = ListQuery.Parse(null, null, new string('a', 255));

        Assert.Null(query);
        Assert.NotNull(errors);
        Assert.Equal(new[] { "search" }, errors.Keys.ToArray());
        Assert.NotEmpty(errors["search"]);
    }

    // Pins that search and page errors accumulate: kills early-return mutants in Parse.
    [Fact]
    public void Parse_SearchTooLongAndPageInvalid_ReportsBothKeys_AC034()
    {
        var (query, errors) = ListQuery.Parse("0", null, new string('a', 255));

        Assert.Null(query);
        Assert.NotNull(errors);
        Assert.Equal(new[] { "page", "search" }, errors.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    // Guard: passes at RED (no length rule yet). Protects the limit being measured after trimming,
    // not on the raw value, and 254 being inclusive.
    [Fact]
    public void Parse_Search254WithWhitespace_IsValid_AC034()
    {
        var term = new string('a', 254);

        var (query, errors) = ListQuery.Parse(null, null, $"  {term}\t ");

        Assert.Null(errors);
        Assert.NotNull(query);
        Assert.Equal(term, query.Search);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("\t ")]
    [InlineData(null)]
    public void Parse_BlankOrAbsentSearch_YieldsNullSearch_AC031(string? search)
    {
        var (query, errors) = ListQuery.Parse(null, null, search);

        Assert.Null(errors);
        Assert.NotNull(query);
        Assert.Null(query.Search);
    }

    [Fact]
    public void Parse_SearchWithSurroundingSpaces_IsTrimmed_AC032()
    {
        var (query, errors) = ListQuery.Parse(null, null, " ada ");

        Assert.Null(errors);
        Assert.NotNull(query);
        Assert.Equal("ada", query.Search);
    }

    // Guard (T-13 review): already passes at RED; protects the long arithmetic in TryGetSkip.
    [Fact]
    public void TryGetSkip_HugePage_ReturnsFalse_AC026()
    {
        var query = new ListQuery(int.MaxValue, 100, null);

        Assert.False(query.TryGetSkip(out _));
    }
}
