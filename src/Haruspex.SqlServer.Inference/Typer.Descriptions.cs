using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// A result column's description: the database's own text for the one column it is, unchanged, which the IR writes as the field's
// `description` (0.33). Documentation, not a claim, so it is given only where the value is exactly that column's: a bare
// reference, without COLLATE, to a table's column, or to a column of a view, derived table or CTE that is itself one. A value
// computed from a column (UPPER(name)), or read from several, describes something else, and takes none. Why:
// docs/decisions/0128-a-column-s-description-reaches-the-fields-that-are-that-column.md.
internal sealed partial class Typer
{
    /// <summary>The description of the column <paramref name="expression"/> is, unchanged; null when it is anything else, or has none.</summary>
    private string? Described(ScalarExpression expression, ReadScope scope) =>
        expression is ColumnReferenceExpression { Collation: null, ColumnType: ColumnType.Regular } column
            ? binder.Resolve(column, scope.Bound) switch
            {
                TableColumn table => Described(table),
                RelationColumn relation => Described(relation, scope),
                _ => null,
            }
            : null;

    /// <summary>A table column's own description.</summary>
    private string? Described(TableColumn column) => model.Descriptions.Of(column.Table.Name, column.Column.Name);

    /// <summary>The description a relation keeps for one of its columns, which it has when that column is one, unchanged.</summary>
    private static string? Described(RelationColumn relation, ReadScope scope) =>
        scope.Of(relation.Source).Descriptions is { } kept && relation.Ordinal < kept.Count ? kept[relation.Ordinal] : null;
}
