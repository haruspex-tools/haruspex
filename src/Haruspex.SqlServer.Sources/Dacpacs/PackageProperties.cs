using System.Xml.Linq;
using Haruspex.SqlServer.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Sources.Dacpacs;

/// <summary>
/// Reads a package's <c>SqlExtendedProperty</c> elements as the extended properties a script's <c>sp_addextendedproperty</c>
/// sets (<see cref="ExtendedPropertyCalls"/>), so that a folder and its package say the same
/// (docs/decisions/0128-a-column-s-description-reaches-the-fields-that-are-that-column.md). An element is named for what it is
/// set on, then the property: <c>[SqlColumn].[library].[book].[title].[MS_Description]</c>. Its value is the literal the script
/// gave, as SQL (<c>N'The title.'</c>), parsed here so that a quote doubled in it is one.
/// </summary>
internal static class PackageProperties
{
    /// <summary>
    /// The property <paramref name="element"/> sets; null for one on what no output has (a schema, a constraint, an index, the
    /// database), which is passed over without a word, as a script's is when no description is found there.
    /// </summary>
    /// <param name="element">The <c>SqlExtendedProperty</c> element.</param>
    /// <param name="package">The package's file name, for its location.</param>
    /// <param name="views">The views the package defines: a column's host is a view's or else a table's.</param>
    /// <param name="procedures">The procedures it defines: a parameter's host is a procedure's or else a function's.</param>
    public static ExtendedProperty? Read(XElement element, string package, IReadOnlySet<ObjectName> views, IReadOnlySet<ObjectName> procedures)
    {
        var parts = ElementName.Parts(ModelXml.Named(element));
        if (parts.Count < 4)
            return null;
        var (host, name) = (parts[0], parts[^1]);
        var owner = new ObjectName(parts[1], parts[2]);
        PropertyLevel[]? levels = (host, parts.Count) switch
        {
            ("SqlTableBase", 4) => [new("SCHEMA", owner.Schema), new("TABLE", owner.Name)],
            ("SqlView", 4) => [new("SCHEMA", owner.Schema), new("VIEW", owner.Name)],
            ("SqlProcedure", 4) => [new("SCHEMA", owner.Schema), new("PROCEDURE", owner.Name)],
            ("SqlColumn", 5) => [new("SCHEMA", owner.Schema), new(views.Contains(owner) ? "VIEW" : "TABLE", owner.Name), new("COLUMN", parts[3])],
            ("SqlSubroutineParameter", 5) => [new("SCHEMA", owner.Schema), new(procedures.Contains(owner) ? "PROCEDURE" : "FUNCTION", owner.Name), new("PARAMETER", parts[3])],
            _ => null,
        };
        if (levels is null)
            return null;

        var at = PackageLocation.Whole(PackageLocation.File(package, owner), 1).Start;
        var (read, text, why) = Value(ModelXml.Property(element, "Value"));
        return new ExtendedProperty(name, read ? text : null, [.. levels], at) { Unread = why };
    }

    /// <summary>
    /// The value's text, from the literal the package holds; not read, saying why, when it isn't a string or NULL. An XML schema
    /// collection's schema is read the same way (<c>PackageReader</c>).
    /// </summary>
    internal static (bool Read, string? Text, string? Why) Value(string? script)
    {
        if (script is null)
            return (true, null, null);
        using var reader = new StringReader(script);
        var parsed = new TSql170Parser(true).ParseExpression(reader, out var errors);
        return (errors.Count, parsed) switch
        {
            (0, StringLiteral literal) => (true, literal.Value, null),
            (0, NullLiteral) => (true, null, null),
            (0, Literal) => (false, null, "Its value isn't text."),
            _ => (false, null, "Its value isn't a literal."),
        };
    }
}
