using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// What an IF's condition proves on each of its arms, for the walker to narrow its state with (Walker.Narrowed). README.md in
// this folder, "Guards", gives the rules and the observations they rest on (StatementFlowObservationTests.Guards.cs).
internal sealed partial class Typer
{
    /// <summary>
    /// What an <c>IF</c>'s condition proves on one of its arms: on the arm taken that it was TRUE (<paramref name="held"/>), on
    /// the other that it was FALSE or UNKNOWN. The proof is a branch's (<see cref="Branch"/>), read in a statement's scope,
    /// where there is no row: so what it proves is of variables and parameters only.
    /// </summary>
    internal Facts Proves(BooleanExpression condition, bool held, ReadScope scope) =>
        Facts.None.With(held ? Branch([], condition, scope) : Branch([condition], null, scope));

    /// <summary>
    /// The row an <c>IF</c>'s arm proves exists: <c>EXISTS (SELECT … FROM t WHERE …)</c> TRUE on it, or <c>NOT EXISTS</c> not
    /// TRUE, where the subquery reads one table filtered by its WHERE alone and doesn't aggregate (an aggregate's one row is
    /// there whatever matches). Whether it stays for a later read is whether the guard's transaction holds it (<see cref="FoundRow"/>):
    /// a lock the guard keeps, by a locking hint or the isolation level, or the transaction's snapshot, while a transaction the
    /// body began is open (observed: A_lock_the_guard_holds_to_the_end_of_an_open_transaction_keeps_the_row_for_the_read,
    /// Snapshot_isolation_in_an_open_transaction_reads_the_row_the_guard_saw). Null when the arm proves no such row.
    /// </summary>
    internal FoundRow? Finds(BooleanExpression condition, bool held, ReadScope scope)
    {
        if (Exists(condition, held) is not { Subquery.QueryExpression: QuerySpecification query } exists
            || query is not { TopRowFilter: null, OffsetClause: null }
            || MayAggregateWithoutGrouping(query))
        {
            return null;
        }

        var entered = Enter(query, scope);
        if (cardinality.Read(query, entered, every: false) is not var (source, table, equalities) || equalities.Count == 0)
            return null;

        // A table hint overrides the level for its table, so the level holds the row only for a guard whose hints keep its
        // lock (observed: A_hint_on_the_guard_that_lowers_the_level_holds_no_lock_for_the_read), and the snapshot only for a
        // guard that reads it, with no hint (A_guard_that_reads_past_the_snapshot_can_see_a_row_the_snapshot_read_does_not).
        var state = scope.State;
        List<TableHintKind> hints = source.Syntax is NamedTableReference named ? [.. named.TableHints.Select(h => h.HintKind)] : [];
        var locks = hints.Any(HoldsLock)
            || (state.Isolation is IsolationLevel.Serializable or IsolationLevel.RepeatableRead && hints.All(KeepsLevelsLock));
        var snapshot = !locks && state.Isolation == IsolationLevel.Snapshot && hints.Count == 0;
        var open = state.Transactions > 0 && (locks || snapshot);
        return new FoundRow(table.Name, equalities, At(exists), open, open && snapshot);
    }

    /// <summary>Whether a table hint was observed to hold the guard's lock on the row until its transaction ends.</summary>
    private static bool HoldsLock(TableHintKind hint) =>
        hint is TableHintKind.UpdLock or TableHintKind.HoldLock or TableHintKind.Serializable or TableHintKind.RepeatableRead or TableHintKind.XLock;

    /// <summary>
    /// Whether a table hint leaves the lock the level takes on the row it reads: one that holds a lock itself, or READPAST, which
    /// only skips rows other sessions have locked (observed in A_lock_the_guard_holds_to_the_end_of_an_open_transaction_keeps_the_row_for_the_read).
    /// </summary>
    private static bool KeepsLevelsLock(TableHintKind hint) => HoldsLock(hint) || hint == TableHintKind.ReadPast;

    /// <summary>The EXISTS an arm proves TRUE: <paramref name="held"/> is whether the arm needs the condition TRUE or not TRUE.</summary>
    private static ExistsPredicate? Exists(BooleanExpression condition, bool held) => condition switch
    {
        BooleanParenthesisExpression parenthesis => Exists(parenthesis.Expression, held),
        BooleanNotExpression not => Exists(not.Expression, !held),
        ExistsPredicate exists when held => exists,
        BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.And } both when held =>
            Exists(both.FirstExpression, held) ?? Exists(both.SecondExpression, held),
        BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.Or } either when !held =>
            Exists(either.FirstExpression, held) ?? Exists(either.SecondExpression, held),
        _ => null,
    };

    /// <summary>
    /// Whether an <c>IF</c>'s arm is taken only when <c>@@ROWCOUNT</c> is not 0: were it 0, the condition could not take the
    /// truth value the arm needs (TRUE for <paramref name="held"/>, FALSE or UNKNOWN otherwise). <c>@@ROWCOUNT</c> is never
    /// NULL or below 0, so a comparison of it with an integer literal is TRUE or FALSE at 0; anything else in the condition
    /// may be anything, and AND, OR and NOT combine as in <see cref="Possible"/>.
    /// </summary>
    internal static bool ProvesRows(BooleanExpression condition, bool held) =>
        (AtNoRows(condition) & (held ? Truth.True : Truth.False | Truth.Unknown)) == Truth.None;

    /// <summary>What <paramref name="condition"/> can be when <c>@@ROWCOUNT</c> is 0, whatever every other value is.</summary>
    private static Truth AtNoRows(BooleanExpression condition) => condition switch
    {
        BooleanParenthesisExpression parenthesis => AtNoRows(parenthesis.Expression),
        BooleanNotExpression not => Not(AtNoRows(not.Expression)),
        BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.And } and =>
            Combine(AtNoRows(and.FirstExpression), AtNoRows(and.SecondExpression), And),
        BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.Or } or =>
            Combine(AtNoRows(or.FirstExpression), AtNoRows(or.SecondExpression), Or),
        BooleanComparisonExpression comparison when RowCount(comparison.FirstExpression) && Integer(comparison.SecondExpression) is { } right =>
            Compare(0, comparison.ComparisonType, right),
        BooleanComparisonExpression comparison when Integer(comparison.FirstExpression) is { } left && RowCount(comparison.SecondExpression) =>
            Compare(left, comparison.ComparisonType, 0),
        _ => Truth.Any,
    };

    private static bool RowCount(ScalarExpression expression) =>
        Strip(expression) is GlobalVariableExpression { Name: var name } && name.Equals("@@ROWCOUNT", StringComparison.OrdinalIgnoreCase);

    private static long? Integer(ScalarExpression expression) =>
        Strip(expression) is IntegerLiteral literal && long.TryParse(literal.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>A comparison of two integers: TRUE or FALSE, or anything for a comparison not listed.</summary>
    private static Truth Compare(long left, BooleanComparisonType type, long right)
    {
        bool? holds = type switch
        {
            BooleanComparisonType.Equals => left == right,
            BooleanComparisonType.NotEqualToBrackets or BooleanComparisonType.NotEqualToExclamation => left != right,
            BooleanComparisonType.LessThan => left < right,
            BooleanComparisonType.LessThanOrEqualTo or BooleanComparisonType.NotGreaterThan => left <= right,
            BooleanComparisonType.GreaterThan => left > right,
            BooleanComparisonType.GreaterThanOrEqualTo or BooleanComparisonType.NotLessThan => left >= right,
            _ => null,
        };
        return holds switch
        {
            true => Truth.True,
            false => Truth.False,
            null => Truth.Any,
        };
    }
}
