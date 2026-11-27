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
    /// The level type of an owner a column, an index or a parameter is on, when the package being read doesn't define it: a package
    /// may set a property on what another package of the same database defines, so <see cref="SchemaDeclarations.ToModel"/> settles
    /// it once every package is read (<see cref="Settle"/>).
    /// </summary>
    internal const string UnsettledOwner = "?";

    /// <summary>
    /// The property <paramref name="element"/> sets, with the levels a script's call would give it; null only for one on the
    /// database itself, which no output has. A host haruspex has no level for keeps its element type as the level type, so that a
    /// reader of the property can say it is there, as it can for a script's.
    /// </summary>
    /// <param name="element">The <c>SqlExtendedProperty</c> element.</param>
    /// <param name="package">The package's file name, for its location.</param>
    /// <param name="defined">
    /// What the package defines. A package names a table's, a view's and a table function's column alike
    /// (<c>[SqlColumn].[shop].[f_lines].[doc].[…]</c>), and a procedure's and a function's parameter alike (observed with
    /// Microsoft.Build.Sql 2.3.0), so the owner says which; one the package doesn't define is settled later, never guessed.
    /// </param>
    public static ExtendedProperty? Read(XElement element, string package, PackageObjects defined)
    {
        ArgumentNullException.ThrowIfNull(defined);
        var parts = ElementName.Parts(ModelXml.Named(element));
        if (parts.Count < 3)
            return null;
        var (host, name) = (parts[0], parts[^1]);
        var owner = new ObjectName(parts[1], parts.Count > 3 ? parts[2] : "");
        PropertyLevel[] levels = (host, parts.Count) switch
        {
            ("SqlSchema", 3) => [new("SCHEMA", parts[1])],
            ("SqlTableBase", 4) => [new("SCHEMA", owner.Schema), new("TABLE", owner.Name)],
            ("SqlView", 4) => [new("SCHEMA", owner.Schema), new("VIEW", owner.Name)],
            ("SqlProcedure", 4) => [new("SCHEMA", owner.Schema), new("PROCEDURE", owner.Name)],
            ("SqlFunction", 4) => [new("SCHEMA", owner.Schema), new("FUNCTION", owner.Name)],
            ("SqlType", 4) => [new("SCHEMA", owner.Schema), new("TYPE", owner.Name)],
            ("SqlColumn", 5) => [new("SCHEMA", owner.Schema), new(defined.ColumnOwner(owner), owner.Name), new("COLUMN", parts[3])],
            ("SqlIndexBase", 5) => [new("SCHEMA", owner.Schema), new(defined.ColumnOwner(owner), owner.Name), new("INDEX", parts[3])],
            ("SqlTableTypeColumn", 5) => [new("SCHEMA", owner.Schema), new("TYPE", owner.Name), new("COLUMN", parts[3])],
            ("SqlSubroutineParameter", 5) => [new("SCHEMA", owner.Schema), new(defined.ParameterOwner(owner), owner.Name), new("PARAMETER", parts[3])],
            _ => [new("SCHEMA", parts[1]), .. parts.Skip(2).SkipLast(1).Select(part => new PropertyLevel(host, part))],
        };

        var at = PackageLocation.Whole(PackageLocation.File(package, owner), 1).Start;
        var (read, text, why) = Value(ModelXml.Property(element, "Value"));
        return new ExtendedProperty(name, read ? text : null, [.. levels], at) { Unread = why };
    }

    /// <summary>
    /// <paramref name="property"/> with an owner the package that set it doesn't define settled against the whole database, by
    /// <paramref name="columnOwner"/> for a column or an index (a table, a view or a table function) and
    /// <paramref name="parameterOwner"/> for a parameter (a procedure or a function), each null where nothing defines it. An
    /// owner nothing defines is taken as a table's or a procedure's, which a reader then finds missing and says so, as it does for
    /// a script's call on what the source doesn't define.
    /// </summary>
    internal static ExtendedProperty Settle(ExtendedProperty property, Func<ObjectName, string?> columnOwner, Func<ObjectName, string?> parameterOwner)
    {
        if (property.Levels is not [var schema, { Type: UnsettledOwner } owner, var part])
            return property;
        var name = new ObjectName(schema.Name, owner.Name);
        var type = part.Type == "PARAMETER" ? parameterOwner(name) ?? "PROCEDURE" : columnOwner(name) ?? "TABLE";
        return property with { Levels = [schema, owner with { Type = type }, part] };
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
