using Haruspex.Collections;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// What reading a write's <c>OUTPUT</c> knows of the write (<see cref="ReadScope.Output"/>): the actions a <c>MERGE</c> takes,
/// the sources that name the write's target, and the sources of a <c>MERGE</c>'s source that a row it changes may not match.
/// README.md in this folder ("DML") gives the rules and their observations.
/// </summary>
/// <param name="Actions">
/// A MERGE's actions, the closed set its <c>$action</c> is drawn from; null for any other write, or a MERGE with an action not
/// modelled.
/// </param>
/// <param name="Target">
/// The sources that name the write's target through its alias in FROM, which OUTPUT may read, but whether as the row before or
/// after the write is not observed, so a column read through one is unknown. Compared by identity where read.
/// </param>
/// <param name="Unmatched">The sources of a MERGE's source, whose columns are null for a row it changes that the source doesn't match.</param>
/// <param name="UnmatchedBecause">Why those columns may be null; null when the MERGE has no clause for such rows.</param>
internal sealed record OutputReading(
    EquatableArray<string>? Actions,
    EquatableArray<BoundSource> Target,
    EquatableArray<BoundSource> Unmatched,
    IrNullReason? UnmatchedBecause);
