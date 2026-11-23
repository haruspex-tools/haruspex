using System.Collections.Immutable;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The clauses of a <see cref="FunctionCall"/>: each property that is a clause, of the call or of a clause of it (<c>GRAPH
/// PATH</c> is <see cref="WithinGroupClause"/>'s), how a call shows it, and how it is written. <c>ClauseTableTests</c> holds the list to ScriptDom's properties, so a property a new version of ScriptDom adds
/// fails a test until it is listed here or among the properties every rule reads (<see cref="AcceptedNodes"/>).
/// </summary>
internal static class CallClauses
{
    /// <summary>A clause, the node (by its type's name) and property that hold it, how SQL writes it, and whether a call carries it.</summary>
    internal sealed record Entry(Clause Clause, string Node, string Property, string Written, Func<FunctionCall, bool> Present);

    /// <summary>Every clause, in the order a call's are named.</summary>
    internal static ImmutableArray<Entry> All { get; } =
    [
        new(Clause.Over, nameof(FunctionCall), nameof(FunctionCall.OverClause), "OVER", c => c.OverClause is not null),

        // ALL is the default, written out: it is the call without the clause (observed:
        // Distinct_and_all_in_an_aggregate_keep_its_type_and_when_it_is_null).
        new(Clause.Distinct, nameof(FunctionCall), nameof(FunctionCall.UniqueRowFilter), "DISTINCT", c => c.UniqueRowFilter == UniqueRowFilter.Distinct),
        new(Clause.WithinGroup, nameof(FunctionCall), nameof(FunctionCall.WithinGroupClause), "WITHIN GROUP", c => c.WithinGroupClause is not null),
        new(Clause.NullTreatment, nameof(FunctionCall), nameof(FunctionCall.IgnoreRespectNulls), "IGNORE NULLS or RESPECT NULLS", c => c.IgnoreRespectNulls is { Count: > 0 }),
        new(Clause.TrimSide, nameof(FunctionCall), nameof(FunctionCall.TrimOptions), "LEADING, TRAILING or BOTH", c => c.TrimOptions is not null),
        new(Clause.KeyValuePairs, nameof(FunctionCall), nameof(FunctionCall.JsonParameters), "key: value", c => c.JsonParameters is { Count: > 0 }),
        new(Clause.JsonOrderBy, nameof(FunctionCall), nameof(FunctionCall.JsonOrderByClause), "ORDER BY", c => c.JsonOrderByClause is not null),
        new(Clause.NullOnNull, nameof(FunctionCall), nameof(FunctionCall.AbsentOrNullOnNull), "NULL ON NULL or ABSENT ON NULL", c => c.AbsentOrNullOnNull is { Count: > 0 }),
        new(Clause.Returning, nameof(FunctionCall), nameof(FunctionCall.ReturnType), "RETURNING", c => c.ReturnType is { Count: > 0 }),
        new(Clause.ArrayWrapper, nameof(FunctionCall), nameof(FunctionCall.WithArrayWrapper), "WITH ARRAY WRAPPER", c => c.WithArrayWrapper),

        // Read by no row: the server refuses it with a percentile (10753), and STRING_AGG's is not observed.
        new(Clause.GraphPath, nameof(WithinGroupClause), nameof(WithinGroupClause.HasGraphPath), "WITHIN GROUP (GRAPH PATH)", c => c.WithinGroupClause is { HasGraphPath: true }),
    ];

    /// <summary>The clauses a call carries.</summary>
    internal static Clause Of(FunctionCall call)
    {
        var clauses = Clause.None;
        foreach (var entry in All)
        {
            if (entry.Present(call))
                clauses |= entry.Clause;
        }

        return clauses;
    }

    /// <summary>Clauses as SQL writes them, joined: <c>DISTINCT, WITHIN GROUP</c>.</summary>
    internal static string Written(Clause clauses) =>
        string.Join(", ", All.Where(e => (clauses & e.Clause) != Clause.None).Select(e => e.Written));
}
