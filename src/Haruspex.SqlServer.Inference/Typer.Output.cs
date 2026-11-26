using System.Collections.Immutable;
using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// A write's OUTPUT, read as a query over the write's sources and inserted and deleted, with what the write tells it
// (OutputReading). README.md in this folder ("DML") gives the model; the observations it rests on are DmlObservationTests
// in the differential tests. The write itself is the Walker's (Walker.Writes.cs).
internal sealed partial class Typer
{
    /// <summary>
    /// What <c>inserted</c> or <c>deleted</c> holds: the target's columns in <paramref name="statement"/>'s state, which for a permanent
    /// table is its declaration (every value is converted to the column, observed:
    /// Output_columns_are_the_values_converted_to_the_target_columns), and null too for the MERGE actions that give it none.
    /// </summary>
    /// <param name="spec">The write.</param>
    /// <param name="target">Its target, as written.</param>
    /// <param name="through">The table a write through a CTE or derived table changes, or why it isn't known (<see cref="Binder.WriteThrough"/>); null for any other write.</param>
    /// <param name="statement">The scope of the state it is read in: after the write for <c>inserted</c>, before it for <c>deleted</c>.</param>
    /// <param name="merged">Why a MERGE's action gives it no row, when it has such an action.</param>
    /// <param name="at">The statement.</param>
    internal LocalRelationValues Pseudo(DataModificationSpecification spec, DmlTarget target, WrittenThrough? through, ReadScope statement, IrNullReason? merged, SourceLocation at)
    {
        LocalRelationValues values;
        if (target.Direct is { } direct)
        {
            values = statement.Names.Find(direct.Text) is { } relation ? Holding(relation, hinted: false, statement) : LocalRelationValues.Unknown(Unsupported(IrUnknownReason.UnresolvedTable, $"{direct} is not declared.", at));
        }
        else if (Written(target, statement) is Table table)
        {
            var source = new BoundSource(table.Name.Name, new TableTarget(table), null, spec.Target);
            values = new LocalRelationValues([.. table.Columns.Select(c => c.Name)], [.. table.Columns.Select(c => FromColumn(new TableColumn(source, table, c), statement))], null);
        }
        else if (through is { Table: { } underneath } written)
        {
            // The table's columns after or before the write, as the table declares them, under the names the CTE gives them: not
            // the CTE's projection, whose WHERE held before the write and may not after it.
            var source = new BoundSource(underneath.Name.Name, new TableTarget(underneath), null, spec.Target);
            values = new LocalRelationValues([.. written.Columns.Select(c => c.Name)], [.. written.Columns.Select(c => FromColumn(new TableColumn(source, underneath, c.Column), statement))], null);
        }
        else
        {
            values = LocalRelationValues.Unknown(Unsupported(IrUnknownReason.UnsupportedStatement, target.Named is { } named
                ? $"The OUTPUT at {at} of a write to {Text(named)}, which is not one table of the project (a view, a synonym, a table defined elsewhere or more than one), is not modelled."
                : $"The OUTPUT at {at} of a write through a CTE or derived table, {Text(spec.Target)}, is not modelled: {through?.Why ?? "its source is not followed"}.", at));
        }

        if (merged is null || values.Values is null)
            return values;
        return values with { Values = [.. values.Values.Select(v => v.Shape is UnknownShape ? v : v.WithNullableBecause(NullReasons.Join(v.NullableBecause, [merged])))] };
    }

    /// <summary>
    /// The table a write writes, when it is one table of the project or a table variable, whose declaration says which columns a
    /// star over its <c>inserted</c> and <c>deleted</c> gives; null for a temporary table, a view, a synonym and the rest.
    /// </summary>
    internal ITable? Written(DmlTarget target, ReadScope statement) => target switch
    {
        { Direct: { } direct } => statement.Names.Find(direct.Text) is TableVariable { Declaration: var declared } ? declared : null,
        { Named: { } only } when binder.ResolveWritten(only) is [var name] && model.FindTableRedefinition(name) is null => model.FindTable(name),
        _ => null,
    };

    /// <summary>
    /// The row an OUTPUT's columns give, read as the select list of a query over the write's sources and the
    /// <c>inserted</c> and <c>deleted</c> relations, filtered by its WHERE, as binding binds it (<see cref="Binder.BindOutput"/>).
    /// </summary>
    /// <param name="spec">The write.</param>
    /// <param name="clause">Its OUTPUT or OUTPUT … INTO clause.</param>
    /// <param name="columns">The clause's select list.</param>
    /// <param name="from">The write's FROM.</param>
    /// <param name="where">The write's WHERE.</param>
    /// <param name="with">The write's WITH.</param>
    /// <param name="inserted">What <c>inserted</c> holds, or null when the write has none.</param>
    /// <param name="deleted">What <c>deleted</c> holds, or null when the write has none.</param>
    /// <param name="written">The table written, when a star over it says which columns it gives (<see cref="Written"/>).</param>
    /// <param name="mergeActions">A MERGE's actions, which its <c>$action</c> is one of; null for another write.</param>
    /// <param name="because">Why a MERGE's source is null in the rows it does not match.</param>
    /// <param name="at">The statement's scope, in the state before the write.</param>
    internal Row OutputRow(
        DataModificationSpecification spec,
        TSqlFragment clause,
        IList<SelectElement> columns,
        FromClause? from,
        BooleanExpression? where,
        WithCtesAndXmlNamespaces? with,
        LocalRelationValues? inserted,
        LocalRelationValues? deleted,
        ITable? written,
        EquatableArray<string>? mergeActions,
        IrNullReason because,
        ReadScope at)
    {
        var location = At(clause);
        return WithCtes(with, at, statement =>
        {
            var output = binder.BindOutput(spec, clause, columns, from, where, statement.Names, inserted?.Names, deleted?.Names, written);
            var scope = output.Pseudo.Aggregate(statement, (s, p) => (p.Name == "inserted" ? inserted : deleted) is { } values ? s.Naming(p, values) : s);

            // Every source in the USING tree: a joined USING is null-extended as a whole.
            var reading = new OutputReading(mergeActions, output.Written, output.Unmatched, output.Unmatched.Count > 0 ? because : null);
            return Query(output.Query, scope.Outputting(reading));
        }, why => new Row([new RowColumn(null, Value.Unknown(why), location)], null));
    }

    /// <summary>
    /// <c>$action</c> in a MERGE's OUTPUT: an <c>nvarchar(10)</c>, never null (observed: Merge_action_is_an_nvarchar_10), one of
    /// the actions the statement has.
    /// </summary>
    private Value MergeAction(ColumnReferenceExpression action, ReadScope scope)
    {
        var at = At(action);
        if (scope.Output?.Actions is not { } mergeActions)
            return Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"{Text(action)} outside a MERGE's OUTPUT is not modelled.", at));
        var shape = (ScalarShape)SqlTypes.ToConvertedShape(new ScalarType(BuiltInType.NVarChar, 10), at);
        var values = new IrValueSet([.. mergeActions.Select(a => new IrValue(a, IsString: true))], IrValueMatch.Exact, [new IrFactSource(IrFactSourceKind.Engine, "$action")]);
        return Value.Computed(shape with { Type = shape.Type with { Facets = (shape.Type.Facets ?? new IrFacets()) with { Values = values } } }, []);
    }

    /// <summary>Whether a column read in an OUTPUT names the write's target through its alias, which is not modelled.</summary>
    private static bool ReadsOutputTarget(Resolution binding, ReadScope scope)
    {
        var source = binding switch
        {
            TableColumn column => column.Source,
            RelationColumn local => local.Source,
            _ => null,
        };
        return source is not null && scope.Output is { } output && output.Target.Contains(source, ReferenceEqualityComparer.Instance);
    }

    /// <summary>Why a column of a MERGE's source may be null in its OUTPUT: the rows it changes not matched by the source have none.</summary>
    private static IrNullReason? Unmatched(BoundSource source, ReadScope scope) =>
        scope.Output is { UnmatchedBecause: { } why } output && output.Unmatched.Contains(source, ReferenceEqualityComparer.Instance) ? why : null;
}
