using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.Tests.Model;

/// <summary>
/// An object's name compares as SQL Server compares identifiers in a case-insensitive database: two spellings that differ in
/// case, or in brackets the parser takes away, are one name. It keys the caches and convergence of procedures and functions,
/// so its equality keeps the laws.
/// </summary>
public class ObjectNameTests
{
    [Fact]
    public void Names_that_differ_in_case_or_brackets_are_one_name()
    {
        var plain = Parsed("sales.get_order");

        Assert.Equal(plain, Parsed("[Sales].[GET_ORDER]"));
        Assert.Equal(plain, new ObjectName("SALES", "Get_Order"));
        Assert.Equal(plain.GetHashCode(), Parsed("[Sales].[GET_ORDER]").GetHashCode());
        Assert.NotEqual(plain, Parsed("sales.get_orders"));
        Assert.NotEqual(plain, Parsed("dbo.get_order"));
    }

    [Fact]
    public void A_name_written_without_its_schema_names_dbo_where_it_is_defined() =>
        Assert.Equal(new ObjectName("dbo", "orders"), Parsed("[Orders]"));

    [Fact]
    public void Its_equality_keeps_the_laws() =>
        EqualityLaws.Hold([
            new ObjectName("sales", "orders"), new ObjectName("SALES", "Orders"), Parsed("[sales].[orders]"),
            new ObjectName("sales", "lines"), new ObjectName("dbo", "orders"), new ObjectName("Dbo", "ORDERS"),
        ]);

    private static ObjectName Parsed(string name)
    {
        var parsed = new TSql170Parser(true).ParseSchemaObjectName(new StringReader(name), out var errors);
        Assert.Empty(errors);
        return SyntaxReader.Name(parsed);
    }
}
