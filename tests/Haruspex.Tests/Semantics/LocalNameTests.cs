using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.Tests.Semantics;

/// <summary>
/// A name a body declares or creates, a variable's or a temporary table's, compares as SQL Server compares it: ignoring case,
/// and without the brackets the parser takes away. The walk keys what it holds by it, so it keeps the laws of equality.
/// </summary>
public class LocalNameTests
{
    [Fact]
    public void Names_that_differ_in_case_or_brackets_are_one_name()
    {
        Assert.Equal(new LocalName("#lines"), Temporary("[#Lines]"));
        Assert.Equal(new LocalName("@Total"), new LocalName("@total"));
        Assert.Equal(new LocalName("@Total").GetHashCode(), new LocalName("@total").GetHashCode());
        Assert.NotEqual(new LocalName("#lines"), new LocalName("@lines"));
        Assert.Equal("[#Lines]".Trim('[', ']'), Temporary("[#Lines]").Text);
    }

    [Fact]
    public void Its_equality_keeps_the_laws() =>
        EqualityLaws.Hold([new LocalName("@total"), new LocalName("@TOTAL"), new LocalName("@Total"), new LocalName("#total"), Temporary("[#Total]"), new LocalName("@other")]);

    private static LocalName Temporary(string name)
    {
        var parsed = new TSql170Parser(true).ParseSchemaObjectName(new StringReader(name), out var errors);
        Assert.Empty(errors);
        return Binder.Temporary(parsed) ?? throw new ArgumentException($"{name} is not a local temporary table.", nameof(name));
    }
}
