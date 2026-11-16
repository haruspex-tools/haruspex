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
