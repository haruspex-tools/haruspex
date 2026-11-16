using Haruspex.Collections;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// Reads of a row an existence guard found (FoundRow): which query reads it, and whether the row is still held for the read.
// Typer.Guards.cs reads the guard; README.md in this folder, "Guards", gives the rules and their observations.
internal sealed partial class Cardinality
{
    /// <summary>
    /// What a query reads, when it reads one table, not its history, filtered by WHERE alone: the source, the table, and the
    /// conjuncts of WHERE that compare a column of it with <c>=</c> to a variable or a literal (<see cref="FoundRow"/>). With
    /// <paramref name="every"/>, every conjunct must be one, so that nothing else filters the rows; null otherwise.
    /// </summary>
    internal (BoundSource Source, Table Table, EquatableArray<FoundRow.Equality> Equalities)? Read(QuerySpecification query, ReadScope scope, bool every)
    {
        if (query is not { GroupByClause: null, HavingClause: null, FromClause.TableReferences: [NamedTableReference] }
            || scope.Bound.Sources is not [{ Target: TableTarget { Table: var table }, History: false } source])
        {
            return null;
        }

        var equalities = new List<FoundRow.Equality>();
        foreach (var conjunct in query.WhereClause?.SearchCondition is { } where ? Conjuncts(where) : [])
        {
            if (Equality(conjunct, source, scope) is { } equality)
                equalities.Add(equality);
            else if (every)
                return null;
        }

        return (source, table, [.. equalities]);
    }

    /// <summary>A conjunct that compares a column of <paramref name="source"/> with <c>=</c> to a variable or a literal.</summary>
    private FoundRow.Equality? Equality(BooleanExpression conjunct, BoundSource source, ReadScope scope)
    {
        if (conjunct is not BooleanComparisonExpression { ComparisonType: BooleanComparisonType.Equals } comparison)
            return null;
        foreach (var (column, other) in new[] { (comparison.FirstExpression, comparison.SecondExpression), (comparison.SecondExpression, comparison.FirstExpression) })
        {
            if (Typer.Strip(column) is not ColumnReferenceExpression reference
                || binder.Resolve(reference, scope.Bound) is not TableColumn bound
                || !ReferenceEquals(bound.Source, source))
            {
                continue;
            }

            var name = bound.Column.Name.ToUpperInvariant();
            switch (Typer.Strip(other))
            {
                case VariableReference variable:
                    return new(name, Binder.Resolve(variable, scope.Names).Name, null);
                case Literal literal and not NullLiteral:
                    return new(name, null, $"{literal.LiteralType}{(literal is StringLiteral { IsNational: true } ? "N" : "")}:{literal.Value}");
            }
        }

        return null;
    }

    /// <summary>
    /// The row an existence guard found that a query reads, if it is a read of one: the guard's table, every conjunct of its
    /// WHERE one of the guard's equalities, so that the guard's row passes it. One still held for the read comes first.
    /// </summary>
    internal (FoundRow Found, bool Held)? Guard(QuerySpecification query, ReadScope scope)
    {
        if (scope.State.Found.Count == 0 || Read(query, scope, every: true) is not var (source, table, equalities) || equalities.Count == 0)
            return null;
        (FoundRow Found, bool Held)? best = null;
        foreach (var found in scope.State.Found.Where(f => f.Table == table.Name && equalities.All(f.Equalities.Contains)))
        {
            var held = Holds(found, source, scope);
            if (best is null || (held && !best.Value.Held))
                best = (found, held);
        }

        return best;
    }

    /// <summary>
    /// Whether the read still sees the row the guard found. A lock the guard holds keeps the row whatever the read is; a
    /// snapshot keeps it only for a read at snapshot isolation without a table hint (observed:
    /// Snapshot_isolation_in_an_open_transaction_reads_the_row_the_guard_saw).
    /// </summary>
    private static bool Holds(FoundRow found, BoundSource source, ReadScope scope) =>
        found.Held
        && (!found.Snapshot || (scope.State.Isolation == IsolationLevel.Snapshot && source.Syntax is NamedTableReference { TableHints.Count: 0 }));
}
