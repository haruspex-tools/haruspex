using System.Collections.Immutable;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// What the conditions being read prove: the filters of each query whose select list is being read (<c>WHERE</c>, an inner
/// join's <c>ON</c>, <c>HAVING</c>; Refinement.cs) and the conditions of each <c>CASE</c> or <c>IIF</c> branch being read
/// (ProcedureAnalyzer.Case.cs). One immutable environment, with one set of lookups: entering a query or a branch extends it
/// (<see cref="With"/>), leaving gives back the one before. The guide's chapter <c>docs/internals/06-flow-joins-and-fixed-points.md</c>,
/// "What conditions prove", explains it.
/// </summary>
/// <remarks>
/// A filter and a branch prove different things, which <see cref="Proof"/> keeps apart. A filter proves a column non-null
/// wherever its source's row is there, so an outer join around the query may still null-extend it; a branch tested the
/// value itself, in the same row after any grouping, so a column it proves is non-null whatever else made it nullable
/// (observed: A_case_branch_runs_only_when_its_condition_is_true_and_later_ones_only_when_earlier_were_not). Sources are
/// compared by reference, since two sources of one table in two scopes are equal as records.
/// </remarks>
internal sealed class Facts
{
    private readonly ImmutableList<Frame> frames;

    private Facts(ImmutableList<Frame> frames)
    {
        this.frames = frames;
    }

    /// <summary>Nothing proved: outside every query and branch.</summary>
    public static Facts None { get; } = new([]);

    /// <summary>How strongly a column is proved non-null.</summary>
    public enum Proof
    {
        /// <summary>Not proved.</summary>
        None,

        /// <summary>By a filter: non-null wherever its source's row is there, which an outer join may null-extend.</summary>
        WhereTheRowIsThere,

        /// <summary>By a branch that tested the value itself: non-null, whatever else made it nullable.</summary>
        OfTheValue,
    }

    /// <summary>These facts and what <paramref name="found"/> proves: for reading a query's select list, or a branch.</summary>
    public Facts With(Found found) => found.AddedTo(this);

    /// <summary>How strongly a column, by its source and key among the source's columns, is proved non-null.</summary>
    public Proof Proved(BoundSource source, string key) =>
        frames.Where(f => f.Values.TryGetValue(source, out var keys) && keys.Contains(key)).Select(f => f.Strength).Aggregate(Proof.None, Stronger);

    /// <summary>
    /// The stronger of two proofs, by an order written here rather than read from the enum's values: proving the value itself
    /// says more than proving it where the row is there, which says more than nothing.
    /// </summary>
    private static Proof Stronger(Proof a, Proof b) => Rank(a) >= Rank(b) ? a : b;

    private static int Rank(Proof proof) => proof switch
    {
        Proof.None => 0,
        Proof.WhereTheRowIsThere => 1,
        Proof.OfTheValue => 2,
    };

    /// <summary>Whether a source's row is proved there, not null-extended by an outer join.</summary>
    public bool Matched(BoundSource source) => frames.Any(f => f.Rows.Contains(source));

    /// <summary>Whether a variable or parameter is proved non-null: a branch tested it, and it has one value throughout an expression.</summary>
    public bool Proved(string variable) => frames.Any(f => f.Variables.Contains(variable));

    /// <summary>Whether an aggregate of <paramref name="query"/>, by its text, is proved non-null: by HAVING, or by a branch read in that query's row.</summary>
    public bool Proved(QuerySpecification? query, string aggregate) =>
        query is not null && frames.Any(f => ReferenceEquals(f.Query, query) && f.Aggregates.Contains(aggregate));

    /// <summary>
    /// Why a column of <paramref name="source"/> read now may be NULL in a super-aggregate row: its query groups by ROLLUP,
    /// CUBE or GROUPING SETS, and the column is read outside that query's aggregates (<paramref name="aggregating"/>).
    /// </summary>
    public IrNullReason? Rolled(BoundSource source, IReadOnlyList<QuerySpecification?> aggregating) =>
        frames.FirstOrDefault(f => f.Rolled is not null && f.Own.Contains(source) && !aggregating.Any(q => ReferenceEquals(q, f.Query)))?.Rolled;

    /// <summary>
    /// What one query's filters or one branch's conditions proved, as <see cref="Found"/> gathered it. Its sources are sets by
    /// identity, and the environment is read, never compared, so nothing compares two frames.
    /// </summary>
    private sealed class Frame(
        Proof strength,
        QuerySpecification? query,
        ImmutableHashSet<BoundSource> own,
        IrNullReason? rolled,
        ImmutableDictionary<BoundSource, ImmutableHashSet<string>> values,
        ImmutableHashSet<BoundSource> rows,
        ImmutableHashSet<string> variables,
        ImmutableHashSet<string> aggregates)
    {
        public Proof Strength { get; } = strength;

        public QuerySpecification? Query { get; } = query;

        public ImmutableHashSet<BoundSource> Own { get; } = own;

        public IrNullReason? Rolled { get; } = rolled;

        public ImmutableDictionary<BoundSource, ImmutableHashSet<string>> Values { get; } = values;

        public ImmutableHashSet<BoundSource> Rows { get; } = rows;

        public ImmutableHashSet<string> Variables { get; } = variables;

        public ImmutableHashSet<string> Aggregates { get; } = aggregates;
    }

    /// <summary>
    /// What one query's filters, or one branch's conditions, prove: gathered while they are read, then added to the
    /// environment whole by <see cref="With"/>.
    /// </summary>
    /// <param name="strength">A filter's proof or a branch's.</param>
    /// <param name="query">The query whose row is read, whose aggregates may be proved; null outside a query.</param>
    /// <param name="own">The query's own sources, those of its FROM; none for a branch.</param>
    public sealed class Found(Proof strength, QuerySpecification? query, IEnumerable<BoundSource> own)
    {
        private readonly Dictionary<BoundSource, HashSet<string>> values = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<BoundSource> rows = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<string> variables = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> aggregates = new(StringComparer.Ordinal);

        /// <summary>The query's own sources, those of its FROM, not an enclosing query's.</summary>
        public ImmutableHashSet<BoundSource> Own { get; } = ImmutableHashSet.CreateRange<BoundSource>(ReferenceEqualityComparer.Instance, own);

        /// <summary>
        /// Where the query groups by ROLLUP, CUBE or GROUPING SETS, whose super-aggregate rows hold NULL in its grouped
        /// columns (observed: Rollup_cube_and_grouping_sets_add_rows_whose_grouped_columns_are_null); null otherwise.
        /// </summary>
        public IrNullReason? Rolled { get; init; }

        /// <summary>A column proved non-null, by its source and its key among the source's columns.</summary>
        public void Value(BoundSource source, string key)
        {
            if (!values.TryGetValue(source, out var keys))
                values[source] = keys = new HashSet<string>(StringComparer.Ordinal);
            keys.Add(key);
        }

        /// <summary>A source whose row is proved there: a null-extended row has every column NULL.</summary>
        public void Row(BoundSource source) => rows.Add(source);

        /// <summary>A variable or parameter proved non-null.</summary>
        public void Variable(string name) => variables.Add(name);

        /// <summary>An aggregate of the query proved non-null, by its text: each is computed once per group.</summary>
        public void Aggregate(string text) => aggregates.Add(text);

        /// <summary>Forgets every proof, keeping the query's own sources and rollup: for a cursor's row, read again when fetched.</summary>
        public void Forget()
        {
            values.Clear();
            rows.Clear();
            variables.Clear();
            aggregates.Clear();
        }

        internal Facts AddedTo(Facts facts) => new(facts.frames.Add(new Frame(
            strength,
            query,
            Own,
            Rolled,
            values.ToImmutableDictionary<KeyValuePair<BoundSource, HashSet<string>>, BoundSource, ImmutableHashSet<string>>(p => p.Key, p => p.Value.ToImmutableHashSet(StringComparer.Ordinal), ReferenceEqualityComparer.Instance),
            ImmutableHashSet.CreateRange<BoundSource>(ReferenceEqualityComparer.Instance, rows),
            ImmutableHashSet.CreateRange(StringComparer.OrdinalIgnoreCase, variables),
            ImmutableHashSet.CreateRange(StringComparer.Ordinal, aggregates))));
    }
}
