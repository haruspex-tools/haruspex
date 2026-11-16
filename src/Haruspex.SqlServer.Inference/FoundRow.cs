using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// A row an existence guard found: <c>IF NOT EXISTS (SELECT … FROM t WHERE k = @k) RETURN</c> proves a row of <c>t</c> with
/// <c>k = @k</c> was there when the guard ran. Whether it is still there when a later statement reads it is what
/// <see cref="Held"/> says. A later read of it is a query of the same table filtered by nothing but equalities among
/// these (<c>Cardinality.Guard</c>). Docs: decision 0053, "A row an existence guard found".
/// </summary>
/// <remarks>
/// Another session may delete the row between the two statements, under read committed, read committed snapshot, or snapshot
/// isolation without a transaction (observed: A_row_an_existence_guard_saw_may_be_gone_when_a_later_statement_reads_it). It
/// stays for the read only while the guard's transaction is open and either the guard holds a lock on it until then
/// (observed: A_lock_the_guard_holds_to_the_end_of_an_open_transaction_keeps_the_row_for_the_read) or both statements read the
/// transaction's snapshot (observed: Snapshot_isolation_in_an_open_transaction_reads_the_row_the_guard_saw). The body's own
/// writes are not a race but may remove it all the same, so any write that can reach a table ends the fact (<c>Walker</c>),
/// as does an assignment to a variable it compares with.
/// </remarks>
/// <param name="Table">The table the guard read.</param>
/// <param name="Equalities">The guard's WHERE conjuncts that compare a column of it with a variable or a literal.</param>
/// <param name="At">Where the guard is: the reason a later read states names it.</param>
/// <param name="Held">
/// Whether the row is held for later reads until the body's outermost transaction ends: an inner COMMIT of nested
/// transactions ends nothing (observed). Otherwise a later read may find nothing (the race).
/// </param>
/// <param name="Snapshot">Whether it is held by the transaction's snapshot rather than by a lock, so only a read at snapshot isolation sees it.</param>
internal sealed record FoundRow(ObjectName Table, EquatableArray<FoundRow.Equality> Equalities, SourceLocation At, bool Held, bool Snapshot)
{
    /// <summary>A column compared with <c>=</c> to a variable or a literal (its text, so the same literal compares the same).</summary>
    /// <param name="Column">The column's name, in upper case: SQL Server's names of columns are not case-sensitive here.</param>
    /// <param name="Variable">The variable or parameter it is compared with, if it is.</param>
    /// <param name="Literal">Otherwise the literal's kind and text.</param>
    public sealed record Equality(string Column, LocalName? Variable, string? Literal);

    /// <summary>The same row, no longer held: after its transaction ends, or where it meets a path on which it isn't held.</summary>
    public FoundRow Raced => this with { Held = false, Snapshot = false };

    /// <summary>Whether it compares with <paramref name="variable"/>, so that assigning it ends the fact.</summary>
    public bool Reads(LocalName variable) => Equalities.Any(e => e.Variable == variable);
}
