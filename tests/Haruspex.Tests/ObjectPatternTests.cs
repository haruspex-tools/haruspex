using Haruspex.SqlServer.Model;

namespace Haruspex.Tests;

public class ObjectPatternTests
{
    [Theory]
    [InlineData("sales.get_order", "sales", "get_order", true)]
    [InlineData("SALES.Get_Order", "sales", "get_order", true)]
    [InlineData("sales.*", "sales", "anything", true)]
    [InlineData("sales.*", "hr", "anything", false)]
    [InlineData("*.list_*", "hr", "list_people", true)]
    [InlineData("*.list_*", "hr", "get_list", false)]
    [InlineData("sales.get_?", "sales", "get_x", true)]
    [InlineData("sales.get_?", "sales", "get_xy", false)]
    [InlineData("[RDF.].GetPropertyList", "RDF.", "GetPropertyList", true)]
    [InlineData("[a]]b].c", "a]b", "c", true)]
    [InlineData("[sales*].x", "salesman", "x", false)]
    [InlineData("[sales*].x", "sales*", "x", true)]
    public void Matches_two_part_names_case_insensitively(string pattern, string schema, string name, bool expected) =>
        Assert.Equal(expected, ObjectPattern.Parse(pattern).Matches(new ObjectName(schema, name)));

    [Theory]
    [InlineData("get_order")]
    [InlineData("db.sales.get_order")]
    [InlineData("sales.")]
    [InlineData(".x")]
    [InlineData("[sales.x")]
    [InlineData("")]
    public void Rejects_anything_but_a_two_part_name(string pattern)
    {
        Assert.False(ObjectPattern.TryParse(pattern, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}
