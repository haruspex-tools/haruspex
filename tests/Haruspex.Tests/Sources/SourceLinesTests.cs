using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Sources;

/// <summary>
/// A text's lines as a location counts them (<see cref="SourceLines"/>): at CR, LF and CRLF, and at nothing else the parser leaves
/// inside a line, so that line <c>n</c> is the one a location's <c>n</c> means.
/// </summary>
public class SourceLinesTests
{
    [Fact]
    public void Lines_end_at_cr_lf_and_crlf_alone() =>
        Assert.Equal(["a", "b", "c", "d", ""], SourceLines.Of("a\rb\nc\r\nd\n"));

    [Theory]
    [InlineData('\u2028')]
    [InlineData('\u2029')]
    [InlineData('\u0085')]
    [InlineData('\f')]
    public void A_separator_the_parser_keeps_inside_a_line_leaves_the_line_whole(char separator) =>
        Assert.Equal([$"-- a{separator}b", "SELECT 1;"], SourceLines.Of($"-- a{separator}b\nSELECT 1;"));
}
