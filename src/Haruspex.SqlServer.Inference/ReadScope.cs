using System.Collections.Immutable;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Everything an expression's value depends on besides the expression itself: the scope binding gave its query
/// (<see cref="QueryScope"/>), what each relation the scope's sources read holds, the state the statement starts from, and what
/// the conditions around it prove and the construct it is read inside. Binding says which source a name means; this says
/// what that source holds and what is known of it there, so the two never mix: Inference attaches values to what Semantics
/// bound, and never looks a name up itself.
/// </summary>
/// <remarks>
/// Immutable, and passed down rather than kept: a query's scope is entered (<see cref="In"/>), its relations' values attached
/// and its filters' facts added as it is read, and nothing is set before a read and reset after it. A class, not a record:
/// what a source holds is keyed by the source itself, by identity, since two sources of one table in two scopes are equal as
/// records; and nothing compares two scopes.
/// </remarks>
internal sealed class ReadScope
{
    private ReadScope(
        QueryScope bound,
        FlowState state,
        ImmutableDictionary<BoundSource, LocalRelationValues> sources,
        ImmutableDictionary<RelationSymbol, LocalRelationValues> named,
        Facts facts,
        QuerySpecification? query,
        ImmutableList<QuerySpecification?> aggregating,
        TSqlFragment? jsonQueryArgument,
        OutputReading? output,
        bool fetched)
    {
        Bound = bound;
        State = state;
        Sources = sources;
        Named = named;
        Facts = facts;
        Query = query;
        Aggregating = aggregating;
        JsonQueryArgument = jsonQueryArgument;
        Output = output;
        Fetched = fetched;
    }

    /// <summary>The bound scope of the query being read.</summary>
    public QueryScope Bound { get; }

    /// <summary>The state the statement being read starts from: its variables, temporary tables and JSON.</summary>
    public FlowState State { get; }

    /// <summary>What each source read so far holds, for a source whose target is a relation, by the source itself.</summary>
    public ImmutableDictionary<BoundSource, LocalRelationValues> Sources { get; }

    /// <summary>What each CTE of the statement and <c>inserted</c> and <c>deleted</c> hold, by symbol.</summary>
    public ImmutableDictionary<RelationSymbol, LocalRelationValues> Named { get; }

    /// <summary>What the filters of the queries and the conditions of the branches being read prove (<see cref="Inference.Facts"/>).</summary>
    public Facts Facts { get; }

    /// <summary>The query whose select list is being read, whose aggregates may be proved and whose grouping an aggregate reads; null outside one.</summary>
    public QuerySpecification? Query { get; }

    /// <summary>The queries whose aggregate's arguments are being read, innermost last: a column read there is a row's, not a group's.</summary>
    public ImmutableList<QuerySpecification?> Aggregating { get; }

    /// <summary>
    /// The expression <c>json_query</c> is reading, while it is read: a CASE, IIF, CHOOSE or COALESCE there holds JSON whether or
    /// not the server folds it to one result, so its results' JSON is joined.
    /// </summary>
    public TSqlFragment? JsonQueryArgument { get; }

    /// <summary>What reading a write's <c>OUTPUT</c> knows of the write; null outside one.</summary>
    public OutputReading? Output { get; }

    /// <summary>
    /// Whether this is a cursor's query, read where a <c>FETCH</c> reads its row: the row may have changed since it was kept,
    /// so no filter proves anything of it, and a variable it reads holds what it held when the cursor was declared.
    /// </summary>
    public bool Fetched { get; }

    /// <summary>The names in force.</summary>
    public NameEnvironment Names => Bound.Names;

    /// <summary>The scope of a statement, outside any query: no sources, the names in force, the state it starts from, and nothing proved.</summary>
    public static ReadScope Statement(NameEnvironment names, FlowState state) => new(
        QueryScope.Empty(null, names),
        state,
        ImmutableDictionary.Create<BoundSource, LocalRelationValues>(ReferenceEqualityComparer.Instance),
        ImmutableDictionary.Create<RelationSymbol, LocalRelationValues>(ReferenceEqualityComparer.Instance),
        Facts.None,
        query: null,
        aggregating: [],
        jsonQueryArgument: null,
        output: null,
        fetched: false);

    /// <summary>The same values, state and facts in another bound scope: a nested query's, or the one its arguments are read in.</summary>
    public ReadScope In(QueryScope bound) => new(bound, State, Sources, Named, Facts, Query, Aggregating, JsonQueryArgument, Output, Fetched);

    /// <summary>This scope with what a source's relation holds attached.</summary>
    public ReadScope Attached(BoundSource source, LocalRelationValues values) =>
        new(Bound, State, Sources.SetItem(source, values), Named, Facts, Query, Aggregating, JsonQueryArgument, Output, Fetched);

    /// <summary>This scope with what a CTE or a pseudo-table holds, for the rest of the statement.</summary>
    public ReadScope Naming(RelationSymbol relation, LocalRelationValues values) =>
        new(Bound, State, Sources, Named.SetItem(relation, values), Facts, Query, Aggregating, JsonQueryArgument, Output, Fetched);

    /// <summary>This scope reading <paramref name="query"/>'s select list, with what its filters prove added.</summary>
    public ReadScope Selecting(QuerySpecification query, Facts.Found filters) =>
        new(Bound, State, Sources, Named, Facts.With(filters), query, Aggregating, JsonQueryArgument, Output, Fetched);

    /// <summary>This scope reading <paramref name="query"/>'s assignments (<c>SELECT @v = …</c>), whose filters prove nothing kept.</summary>
    public ReadScope Assigning(QuerySpecification query) =>
        new(Bound, State, Sources, Named, Facts, query, Aggregating, JsonQueryArgument, Output, Fetched);

    /// <summary>This scope reading a branch, with what its conditions prove added.</summary>
    public ReadScope Branching(Facts.Found conditions) =>
        new(Bound, State, Sources, Named, Facts.With(conditions), Query, Aggregating, JsonQueryArgument, Output, Fetched);

    /// <summary>This scope reading an aggregate's arguments, of the query whose select list is being read.</summary>
    public ReadScope InAggregate() =>
        new(Bound, State, Sources, Named, Facts, Query, Aggregating.Add(Query), JsonQueryArgument, Output, Fetched);

    /// <summary>This scope reading <paramref name="argument"/> as <c>json_query</c>'s argument.</summary>
    public ReadScope UnderJsonQuery(TSqlFragment argument) =>
        new(Bound, State, Sources, Named, Facts, Query, Aggregating, argument, Output, Fetched);

    /// <summary>This scope reading a write's <c>OUTPUT</c>, which knows what <paramref name="output"/> says of the write.</summary>
    public ReadScope Outputting(OutputReading output) =>
        new(Bound, State, Sources, Named, Facts, Query, Aggregating, JsonQueryArgument, output, Fetched);

    /// <summary>This scope reading a cursor's query where a <c>FETCH</c> reads its row.</summary>
    public ReadScope Fetching() =>
        new(Bound, State, Sources, Named, Facts, Query, Aggregating, JsonQueryArgument, Output, fetched: true);

    /// <summary>What a source's relation holds; the source's values were attached when its query's scope was entered.</summary>
    public LocalRelationValues Of(BoundSource source) =>
        Sources.TryGetValue(source, out var values) ? values : throw new InvalidOperationException($"No values are attached to the source {Syntax.FragmentText.OneLine(source.Syntax)}.");
}
