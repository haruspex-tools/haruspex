using Haruspex.Ir;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>The tables of a project, with each column's type and whether it is proved non-null.</summary>
/// <param name="sources">Each table's CREATE TABLE.</param>
/// <param name="changes">
/// Tables changed after their CREATE TABLE in a way that is not modelled (ALTER COLUMN, DROP TABLE), with why.
/// </param>
/// <param name="others">Other objects a FROM clause can read (views, synonyms, functions), with their kind.</param>
internal sealed class Catalog(
    IReadOnlyDictionary<ObjectName, TableSource> sources,
    IReadOnlyDictionary<ObjectName, UnknownShape> changes,
    IReadOnlyDictionary<ObjectName, string> others)
{
    private readonly Dictionary<ObjectName, TableModel> tables = [];

    /// <summary>Why the table cannot be trusted as declared, or null.</summary>
    public UnknownShape? Change(ObjectName name) => changes.GetValueOrDefault(name);

    /// <summary>
    /// Every object with this name, in any schema, that a FROM clause could read: tables, and the views,
    /// synonyms and functions that are not modelled. In a stable order.
    /// </summary>
    public IReadOnlyList<(ObjectName Name, string Kind)> ReadableNamed(string name) =>
    [
        .. sources.Keys.Select(k => (Name: k, Kind: "table"))
            .Concat(others.Select(o => (Name: o.Key, Kind: o.Value)))
            .Where(c => c.Name.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Name.ToString(), StringComparer.Ordinal)
            .ThenBy(c => c.Kind, StringComparer.Ordinal),
    ];

    /// <summary>The kind of a modelled-elsewhere object (view, synonym, function) with this name, or null.</summary>
    public string? OtherKind(ObjectName name) => others.GetValueOrDefault(name);

    public TableModel? Table(ObjectName name)
    {
        if (tables.TryGetValue(name, out var table))
            return table;
        if (!sources.TryGetValue(name, out var source))
            return null;
        return tables[name] = Build(source);
    }

    private static TableModel Build(TableSource source)
    {
        var definition = source.Definition;
        var primaryKey = definition.TableConstraints
            .OfType<UniqueConstraintDefinition>()
            .Where(u => u.IsPrimaryKey)
            .SelectMany(u => u.Columns)
            .Select(c => c.Column.MultiPartIdentifier.Identifiers[^1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var columns = new List<ColumnModel>();
        foreach (var column in definition.ColumnDefinitions)
        {
            var at = new SourceLocation(source.File, column.StartLine, column.StartColumn);

            // Non-null only when the engine enforces it: NOT NULL, a primary key (which implies NOT NULL), or
            // IDENTITY (which cannot be nullable). A column declared with neither NULL nor NOT NULL is nullable
            // under the default ANSI settings; with ANSI_NULL_DFLT_OFF it would not be, so nullable is the
            // claim that holds either way.
            var notNull = column.Constraints.OfType<NullableConstraintDefinition>().Any(n => !n.Nullable)
                || column.Constraints.OfType<UniqueConstraintDefinition>().Any(u => u.IsPrimaryKey)
                || primaryKey.Contains(column.ColumnIdentifier.Value)
                || column.IdentityOptions is not null;

            var type = column.ComputedColumnExpression is not null
                ? new UnknownShape(IrUnknownReasons.UnsupportedSqlType, $"{source.Name}.{column.ColumnIdentifier.Value} is a computed column, which is not modelled yet.", at)
                : SqlTypes.ToShape(column.DataType, at);

            columns.Add(new ColumnModel(column.ColumnIdentifier.Value, type, notNull));
        }

        return new TableModel(source.Name, columns);
    }
}

internal sealed record TableModel(ObjectName Name, IReadOnlyList<ColumnModel> Columns)
{
    public ColumnModel? Column(string name) =>
        Columns.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

internal sealed record ColumnModel(string Name, Shape Type, bool NotNull);
