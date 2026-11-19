using Haruspex.Collections;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// Where a result column's value came from: the table columns it is read from, which the IR writes as a field's `from` (0.25), so
// that `haruspex explain` can start a field's derivation at its column. A record of provenance, not a claim: nothing is typed from
// it, and an expression it misses only leaves a field's derivation shorter. Why the IR carries it:
// docs/decisions/0078-the-ir-says-where-each-field-came-from.md.
internal sealed partial class Typer
{
    /// <summary>
    /// The table columns <paramref name="expression"/>'s value flows from, in the order it first reads them, each once: a column
    /// read directly, or through a function, an aggregate, arithmetic or a conversion, and a column of a derived table, CTE or
    /// view the relation itself traced. A column only tested (a <c>WHEN</c>, a simple <c>CASE</c>'s input, an <c>IIF</c>'s
    /// condition), only ordering or partitioning a window, or read in a subquery, is not where the value comes from, so it is
    /// left out.
    /// </summary>
    private EquatableArray<IrSourceColumn> Reads(ScalarExpression expression, ReadScope scope)
    {
        var found = new ValueColumns();
        expression.Accept(found);
        var reads = new List<IrSourceColumn>();
        foreach (var column in found.Columns)
        {
            switch (binder.Resolve(column, scope.Bound))
            {
                case TableColumn table:
                    reads.Add(SourceColumn(table));
                    break;
                case RelationColumn relation when scope.Of(relation.Source).From is { } traced && relation.Ordinal < traced.Count:
                    reads.AddRange(traced[relation.Ordinal]);
                    break;
                default:
                    break;
            }
        }

        return [.. reads.Distinct()];
    }

    /// <summary>A table column as the IR names it: as its <see cref="IrNullRule.NullableColumn"/> reason does, with its type and declaration.</summary>
    private IrSourceColumn SourceColumn(TableColumn bound)
    {
        var (_, table, column) = bound;
        var sql = column.IsComputed ? null : (ColumnShape(table, table.Name.ToString(), column) as ScalarShape)?.Type.Sql;
        return new IrSourceColumn($"{table.Name}.{column.Name}", sql, column.At.ToString());
    }

    /// <summary>The column references an expression's value is made from, as <see cref="Reads"/> says.</summary>
    private sealed class ValueColumns : TSqlFragmentVisitor
    {
        public List<ColumnReferenceExpression> Columns { get; } = [];

        public override void Visit(ColumnReferenceExpression node)
        {
            if (node.ColumnType == ColumnType.Regular)
                Columns.Add(node);
        }

        // A condition decides which value, not what it is made of.
        public override void ExplicitVisit(BooleanParenthesisExpression node)
        {
        }

        public override void ExplicitVisit(BooleanComparisonExpression node)
        {
        }

        public override void ExplicitVisit(BooleanBinaryExpression node)
        {
        }

        public override void ExplicitVisit(BooleanNotExpression node)
        {
        }

        public override void ExplicitVisit(BooleanIsNullExpression node)
        {
        }

        public override void ExplicitVisit(BooleanTernaryExpression node)
        {
        }

        public override void ExplicitVisit(LikePredicate node)
        {
        }

        public override void ExplicitVisit(InPredicate node)
        {
        }

        public override void ExplicitVisit(ExistsPredicate node)
        {
        }

        public override void ExplicitVisit(SimpleCaseExpression node)
        {
            foreach (var when in node.WhenClauses)
                when.ThenExpression.Accept(this);
            node.ElseExpression?.Accept(this);
        }

        // A window's ordering and partitions decide which rows, not what the value is made of.
        public override void ExplicitVisit(OverClause node)
        {
        }

        // A subquery's columns are its own query's; its value is the subquery's result, not a column of this one.
        public override void ExplicitVisit(ScalarSubquery node)
        {
        }
    }
}
