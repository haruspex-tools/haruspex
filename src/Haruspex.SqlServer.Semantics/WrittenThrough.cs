using Haruspex.Collections;
using Haruspex.SqlServer.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// What a write through a CTE or a derived table writes (<see cref="Binder.WriteThrough"/>): the one table of the project
/// underneath and the columns the source gives it under its own names, or why that isn't known.
/// </summary>
/// <param name="Table">The table underneath; null when <paramref name="Why"/> says why it isn't known.</param>
/// <param name="Columns">The source's columns, in order, by the names it gives them, each a column of <paramref name="Table"/> unchanged.</param>
/// <param name="Queries">The queries on the way down to the table, outermost first, whose <c>TOP</c> and <c>OFFSET … FETCH</c> limit the rows it reaches.</param>
/// <param name="Why">Why the table isn't known, or null when it is.</param>
public sealed record WrittenThrough(Table? Table, EquatableArray<WrittenColumn> Columns, EquatableArray<QuerySpecification> Queries, string? Why)
{
    /// <summary>Not followed, for the reason given.</summary>
    /// <param name="why">Why, as a clause that ends a sentence.</param>
    public static WrittenThrough Not(string why) => new(null, [], [], why);
}
