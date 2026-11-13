using System.Collections.Immutable;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// What an expression is read in: the scope binding gave its query (<see cref="QueryScope"/>), what each relation the scope's
/// sources read holds, and the state the statement starts from. Binding says which source a name means; this says what that
/// source holds, so the two never mix: Inference attaches values to what Semantics bound, and never looks a name up itself.
/// </summary>
/// <remarks>
/// Immutable, and passed down rather than kept: a query's scope is entered (<see cref="In"/>) and its relations' values
/// attached as it is read, and nothing is set before a statement and reset after it.
/// </remarks>
/// <param name="Bound">The bound scope of the query being read.</param>
/// <param name="State">The state the statement being read starts from: its variables, temporary tables and JSON.</param>
/// <param name="Sources">What each source read so far holds, for a source whose target is a relation, by the source itself.</param>
/// <param name="Named">What each CTE of the statement and <c>inserted</c> and <c>deleted</c> hold, by symbol.</param>
internal sealed record ReadScope(
    QueryScope Bound,
    FlowState State,
    ImmutableDictionary<BoundSource, LocalRelationValues> Sources,
    ImmutableDictionary<RelationSymbol, LocalRelationValues> Named)
{
    /// <summary>The scope of a statement, outside any query: no sources, the names in force, and the state it starts from.</summary>
    public static ReadScope Statement(NameEnvironment names, FlowState state) => new(
        QueryScope.Empty(null, names),
        state,
        ImmutableDictionary.Create<BoundSource, LocalRelationValues>(ReferenceEqualityComparer.Instance),
        ImmutableDictionary.Create<RelationSymbol, LocalRelationValues>(ReferenceEqualityComparer.Instance));

    /// <summary>The same values and state in another bound scope: a nested query's, or the one its arguments are read in.</summary>
    public ReadScope In(QueryScope bound) => this with { Bound = bound };

    /// <summary>This scope with what a source's relation holds attached.</summary>
    public ReadScope Attached(BoundSource source, LocalRelationValues values) => this with { Sources = Sources.SetItem(source, values) };

    /// <summary>This scope with what a CTE or a pseudo-table holds, for the rest of the statement.</summary>
    public ReadScope Naming(RelationSymbol relation, LocalRelationValues values) => this with { Named = Named.SetItem(relation, values) };

    /// <summary>What a source's relation holds; the source's values were attached when its query's scope was entered.</summary>
    public LocalRelationValues Of(BoundSource source) =>
        Sources.TryGetValue(source, out var values) ? values : throw new InvalidOperationException($"No values are attached to the source {Syntax.FragmentText.OneLine(source.Syntax)}.");

    /// <summary>The names in force.</summary>
    public NameEnvironment Names => Bound.Names;
}
