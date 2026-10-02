using MicroCrm.Api.Common;

namespace MicroCrm.Api.Tests.Unit.Common;

public sealed class LikePatternTests
{
    // Backslash must be escaped first, then % and _; the result is wrapped in % for a "contains" match.
    [Theory]
    [InlineData("ada", "%ada%")]
    [InlineData("50%", @"%50\%%")]
    [InlineData("a_b", @"%a\_b%")]
    [InlineData(@"c\d", @"%c\\d%")]
    [InlineData(@"\%", @"%\\\%%")]
    [InlineData("%_", @"%\%\_%")]
    [InlineData(@"\\", @"%\\\\%")]
    [InlineData(@"50%_a\b", @"%50\%\_a\\b%")]
    public void Contains_EscapesBackslashPercentUnderscore_AC033(string term, string expected)
    {
        Assert.Equal(expected, LikePattern.Contains(term));
    }
}
