namespace Haruspex.Tests.Config;

/// <summary>A pattern of one schema's object names: what it matches, and that it is equal to another of the same text.</summary>
public class NamePatternTests
{
    private static NamePattern Parse(string text)
    {
        Assert.True(NamePattern.TryParse(text, out var pattern, out var error), error);
        return pattern;
    }

    [Theory]
    [InlineData("get_order", "GET_ORDER", true)]
    [InlineData("list_*", "list_people", true)]
    [InlineData("list_*", "get_list", false)]
    [InlineData("get_?", "get_x", true)]
    [InlineData("[a*b]", "a*b", true)]
    [InlineData("[a*b]", "axb", false)]
    public void Matches_a_name_of_its_schema_ignoring_case(string pattern, string name, bool expected) =>
        Assert.Equal(expected, Parse(pattern).Matches(name));

    [Fact]
    public void Its_equality_keeps_the_laws() =>
        EqualityLaws.Hold([Parse("list_*"), Parse("list_*"), Parse("LIST_*"), Parse("get_?"), Parse("get_?")]);
}
