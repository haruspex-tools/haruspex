using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// The window an <c>OVER</c> clause reads, its names resolved through its query's <c>WINDOW</c> clause (SQL Server 2022):
/// <c>SUM(qty) OVER w … WINDOW w AS (ORDER BY id)</c>, a window defined on another (<c>w2 AS (w1 ROWS …)</c>, in any order),
/// and <c>OVER (w ROWS …)</c>, which adds to the window it names. Each element (partition, order, frame) comes from the one place
/// along the chain that gives it: the server refuses one given twice (4123), a name not defined (5362) and a cycle (5365), all
/// when the procedure is created. Names compare as other identifiers do, ignoring case. Observed:
/// <c>A_window_named_in_a_window_clause_brings_its_frame_to_every_function_over_it</c>,
/// <c>A_window_defined_on_another_named_window_takes_the_elements_of_both</c>.
/// </summary>
public static class NamedWindows
{
    /// <summary>The window <paramref name="over"/> reads, or why the server refuses it (<see cref="ResolvedWindow.Refused"/>).</summary>
    /// <param name="query">The query whose select list holds the call, whose <c>WINDOW</c> clause defines the names; null when not known.</param>
    /// <param name="over">The call's <c>OVER</c> clause.</param>
    public static ResolvedWindow Resolve(QuerySpecification? query, OverClause over)
    {
        ArgumentNullException.ThrowIfNull(over);
        var partitions = over.Partitions is { Count: > 0 } own ? own : null;
        var order = over.OrderByClause;
        var frame = over.WindowFrameClause;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var name = over.WindowName; name is not null;)
        {
            if (!seen.Add(name.Value))
                return ResolvedWindow.Refusal("SQL Server refuses a window defined on itself (5365).");
            if (query?.WindowClause?.WindowDefinition.FirstOrDefault(d => string.Equals(d.WindowName?.Value, name.Value, StringComparison.OrdinalIgnoreCase)) is not { } definition)
                return ResolvedWindow.Refusal($"SQL Server refuses the window '{name.Value}', which its query's WINDOW clause doesn't define (5362).");
            var givenTwice = (partitions is not null && definition.Partitions is { Count: > 0 }) || (order is not null && definition.OrderByClause is not null)
                || (frame is not null && definition.WindowFrameClause is not null);
            if (givenTwice)
                return ResolvedWindow.Refusal($"SQL Server refuses a partition, order or frame given again where '{name.Value}' gives it (4123).");

            partitions ??= definition.Partitions is { Count: > 0 } theirs ? theirs : null;
            order ??= definition.OrderByClause;
            frame ??= definition.WindowFrameClause;
            name = definition.RefWindowName;
        }

        return new ResolvedWindow(partitions is null ? [] : [.. partitions], order, frame);
    }
}
