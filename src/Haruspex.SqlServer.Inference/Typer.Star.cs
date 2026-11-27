using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// SELECT * and alias.*, wherever a query stands: binding lists the columns (Binder.Star), and each is read as a column
// reference to it would be. README.md in this folder ("SELECT *") gives the rules and the observations they rest on
// (SelectStarObservationTests in the differential tests); decision 0047 says which tables a star is bound against.
internal sealed partial class Typer
{
    /// <summary>
    /// Adds the columns <c>*</c> or <c>alias.*</c> stands for, each read as a column reference to it would be, so it keeps its
    /// type, its facts, its origin and what the query proves of it. Returns why they can't be listed, when they can't; then
    /// nothing is added.
    /// </summary>
    /// <param name="star">The star.</param>
    /// <param name="scope">The scope of the query whose select list it is in.</param>
    /// <param name="columns">The query's columns so far, to add to.</param>
    private UnknownShape? Expand(SelectStarExpression star, ReadScope scope, List<RowColumn> columns)
    {
        var at = At(star);
        return binder.Star(star, scope.Bound) switch
        {
            ExpandedStar { Columns: var expanded } => Add(expanded),
            UnlistedStar { Source: var source } => scope.Of(source).Why is { } why
                ? why with { At = at }
                : throw new InvalidOperationException($"Binding could not list the columns of {Text(source.Syntax)}, whose values are known."),
            UnexpandedStar { Problem: var problem } => Unknown(problem),
        };

        UnknownShape? Add(IEnumerable<StarColumn> expanded)
        {
            columns.AddRange(expanded.Select(c => Read(c) with { Bare = c.Column }));
            return null;
        }

        RowColumn Read(StarColumn c) => c.Column switch
        {
            TableColumn table => new RowColumn(c.Name, FromColumn(table, scope), at, table) { From = [SourceColumn(table)], Description = Described(table) },
            RelationColumn { Relation: XmlNodesCall } => new RowColumn(c.Name, NodeColumn(c.Name, at), at),
            RelationColumn relation => new RowColumn(c.Name, FromLocal(relation.Source, relation.Ordinal, scope, at), at)
            {
                From = scope.Of(relation.Source).From is { } traced && relation.Ordinal < traced.Count ? traced[relation.Ordinal] : [],
                Description = Described(relation, scope),
            },
            UnlistedColumn unlisted => new RowColumn(c.Name, FromLocal(unlisted.Source, -1, scope, at), at),
            Unresolved unresolved => new RowColumn(c.Name, Value.Unknown(Unknown(unresolved.Problem)), at),
        };
    }

    /// <summary>
    /// Why a relation over a query (a CTE, a derived table, a view, an inline function) has no columns binding could list: its
    /// query's star can't be expanded, or its query is unknown as a whole (<see cref="GroupedByAll"/>), or else a column has no name or two share one, which SQL Server refuses (observed:
    /// A_derived_table_or_cte_refuses_two_columns_of_one_name), as it refuses a view's column list of another length.
    /// </summary>
    /// <param name="relation">The relation's name, as messages give it.</param>
    /// <param name="row">Its query's row.</param>
    /// <param name="at">Where the relation is defined.</param>
    private static LocalRelationValues Unlisted(string relation, Row row, SourceLocation at) =>
        LocalRelationValues.Unknown(row.Unlisted is { } why
            ? why with { Detail = $"{relation}: {why.Detail}" }
            : Unsupported(IrUnknownReason.UnsupportedQuery, $"{relation}: its columns cannot be listed: a column has no name of its own, or its column list does not match its query, which SQL Server refuses.", at));
}
