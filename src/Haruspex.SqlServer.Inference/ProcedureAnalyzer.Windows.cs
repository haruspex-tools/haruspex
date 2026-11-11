using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// Slice 6i: window functions, a call with OVER. README.md in this folder ("Window functions") gives the rules and the
// observations they rest on (FunctionObservationTests in the differential tests).
internal sealed partial class ProcedureAnalyzer
{
    /// <summary>
    /// A window function. Its arguments are read as the select list's are, after grouping: a window is computed for each row
    /// the query returns, so an argument's reasons (a ROLLUP's NULL among them) are its own, and a partition is never empty.
    /// <list type="bullet">
    /// <item><c>ROW_NUMBER</c>, <c>RANK</c>, <c>DENSE_RANK</c> and <c>NTILE</c>: bigint, never null (NTILE of NULL or 0 is an
    /// error, 4116).</item>
    /// <item><c>COUNT</c> (int, which could overflow as COUNT without a window does) and <c>COUNT_BIG</c>: never null otherwise,
    /// 0 over an empty frame.</item>
    /// <item><c>SUM</c>, <c>MIN</c> and <c>MAX</c>: typed as the aggregates; NULL when every value in the frame is, so with the
    /// argument's reasons, and over a frame that may be empty (<c>empty-frame</c>), over a column never null too.</item>
    /// <item><c>FIRST_VALUE</c> and <c>LAST_VALUE</c>: the argument with its facts, and <c>empty-frame</c>.</item>
    /// <item><c>LAG</c> and <c>LEAD</c>: see <see cref="Offset"/>.</item>
    /// <item><c>PERCENTILE_CONT</c> (float, over a number or a bit) and <c>PERCENTILE_DISC</c> (its argument's type and facts): NULLs are
    /// skipped, so NULL only when every value in the partition is.</item>
    /// </list>
    /// Observed: Window_functions_are_typed_by_their_kind_or_their_argument,
    /// A_frame_that_may_leave_out_the_current_row_can_be_empty_and_give_null_over_a_never_null_column,
    /// Ranking_is_never_null_and_percentiles_are_null_only_over_all_null_input.
    /// </summary>
    private Value Window(FunctionCall call, BindingScope scope)
    {
        var name = call.FunctionName.Value.ToLowerInvariant();
        var over = call.OverClause;
        IReadOnlyList<IrNullReason> Framed() => MayBeEmpty(over) ? [new IrNullReason(IrNullRules.EmptyFrame, At(call).ToString())] : [];

        switch (name, call.Parameters.Count)
        {
            case ("row_number" or "rank" or "dense_rank", 0):
            case ("ntile", 1):
                return Typed(new ScalarType("bigint"), [], call);

            // COUNT is an int over a frame as without one: past 2,147,483,647 rows it would overflow, which is NULL under a
            // caller's options (count-overflow, as for COUNT: no partition that large can be read to observe it).
            case ("count", 1):
                return Typed(new ScalarType("int"), [new IrNullReason(IrNullRules.CountOverflow, At(call).ToString())], call);
            case ("count_big", 1):
                return Typed(new ScalarType("bigint"), [], call);

            case ("sum", 1):
            {
                var argument = Expression(call.Parameters[0], scope);
                if (argument.Shape is UnknownShape)
                    return argument;
                if (argument.IsLiveJson || TypeOf(argument, call.Parameters[0]) is not { } from || SumType(from) is not { } type)
                    return NotModelled(call, "SUM of a value of this type is not modelled yet.");
                IReadOnlyList<IrNullReason> because = [.. argument.NullableBecause, .. from is { IsDecimal: true, Precision: <= 19 } ? [] : (IReadOnlyList<IrNullReason>)[Overflow(call)], .. Framed()];
                return Typed(type, because, call);
            }

            // AVG and the statistics as without a window (Average): a partition is never empty, but a frame may be, and a
            // sample statistic over a frame of one value is NULL.
            case ("avg" or "stdev" or "stdevp" or "var" or "varp", 1):
            {
                var argument = Expression(call.Parameters[0], scope);
                if (argument.Shape is UnknownShape)
                    return argument;
                if (argument.IsLiveJson || TypeOf(argument, call.Parameters[0]) is not { } from || Statistic(name, from) is not var (type, overflows))
                    return NotModelled(call, $"{name.ToUpperInvariant()} of a value of this type is not modelled.");
                IReadOnlyList<IrNullReason> because =
                [
                    .. argument.NullableBecause,
                    .. name is "stdev" or "var" ? [new IrNullReason(IrNullRules.TooFewValues, At(call).ToString())] : (IReadOnlyList<IrNullReason>)[],
                    .. overflows ? [Overflow(call)] : (IReadOnlyList<IrNullReason>)[],
                    .. Framed(),
                ];
                return Typed(type, because, call);
            }

            case ("min" or "max", 1):
            case ("first_value" or "last_value", 1):
            {
                var argument = Expression(call.Parameters[0], scope);
                if (argument.Shape is UnknownShape)
                    return argument;
                if (argument.IsLiveJson || argument.Shape is not ScalarShape)
                    return NotModelled(call, "a window over a value that is not a scalar is not modelled.");
                return argument with { NullableBecause = [.. argument.NullableBecause, .. Framed()] };
            }

            case ("lag" or "lead", >= 1 and <= 3):
                return Offset(call, scope);

            case ("percentile_cont" or "percentile_disc", 1) when call.WithinGroupClause?.OrderByClause.OrderByElements is [var element]:
            {
                var argument = Expression(element.Expression, scope);
                if (argument.Shape is UnknownShape)
                    return argument;
                if (argument.IsLiveJson || argument.Shape is not ScalarShape)
                    return NotModelled(call, "a percentile of a value that is not a scalar is not modelled.");
                if (name == "percentile_disc")
                    return argument;
                return TypeOf(argument, element.Expression) is { IsNumber: true } or { Name: "bit" }
                    ? Typed(new ScalarType("float"), argument.NullableBecause, call)
                    : NotModelled(call, "PERCENTILE_CONT of a value of this type is not modelled.");
            }

            default:
                return NotModelled(call, "this window function is not modelled yet.");
        }
    }

    /// <summary>
    /// Whether a window's frame may hold no row: only an explicit frame can, and only one that may leave out the current row,
    /// starting after it or ending before it (<c>ROWS BETWEEN 2 PRECEDING AND 1 PRECEDING</c>). Without a frame, the whole
    /// partition (without ORDER BY) or the rows up to the current one's peers (with it) are read, the current row among them.
    /// </summary>
    private static bool MayBeEmpty(OverClause over)
    {
        if (over.WindowFrameClause is not { } frame)
            return false;
        var startsByCurrent = frame.Top.WindowDelimiterType is WindowDelimiterType.UnboundedPreceding or WindowDelimiterType.ValuePreceding or WindowDelimiterType.CurrentRow;
        var endsByCurrent = frame.Bottom is null
            || frame.Bottom.WindowDelimiterType is WindowDelimiterType.CurrentRow or WindowDelimiterType.ValueFollowing or WindowDelimiterType.UnboundedFollowing;
        return !(startsByCurrent && endsByCurrent);
    }

    /// <summary>
    /// <c>LAG(x [, offset [, default]])</c> and <c>LEAD</c>: x's type. Past the partition's edge it is NULL (<c>window-edge</c>),
    /// unless a default is given, which is converted to x's type: a long string cut, <c>1.9</c> to 1, and an overflow an error,
    /// or NULL under a caller's ANSI_WARNINGS and ARITHABORT off. A NULL offset is NULL; offset 0 is the row itself (observed:
    /// Lag_and_lead_are_null_past_the_partitions_edge_unless_a_default_is_given_which_is_converted_to_the_columns_type).
    /// Without a default it gives x's values only, so x's facts; with one, which may be outside them, only the type's.
    /// </summary>
    private Value Offset(FunctionCall call, BindingScope scope)
    {
        var parameters = call.Parameters;
        var value = Expression(parameters[0], scope);
        if (value.Shape is UnknownShape)
            return value;
        if (value.IsLiveJson || value.Shape is not ScalarShape)
            return NotModelled(call, "LAG or LEAD of a value that is not a scalar is not modelled.");

        IReadOnlyList<IrNullReason> offsetBecause = [];
        var itself = false;
        if (parameters.Count >= 2)
        {
            if (Strip(parameters[1]) is IntegerLiteral { Value: var n } && n.TrimStart('0').Length == 0)
            {
                itself = true;
            }
            else
            {
                var offset = Expression(parameters[1], scope);
                if (offset.Shape is UnknownShape)
                    return offset;
                offsetBecause = offset.NullableBecause;
            }
        }

        if (parameters.Count < 3 || Strip(parameters[2]) is NullLiteral)
        {
            IReadOnlyList<IrNullReason> edge = itself ? [] : [new IrNullReason(IrNullRules.WindowEdge, At(call).ToString())];
            return value with { NullableBecause = [.. value.NullableBecause, .. edge, .. offsetBecause] };
        }

        var fallback = Expression(parameters[2], scope);
        if (fallback.Shape is UnknownShape)
            return fallback;
        if (TypeOf(value, parameters[0]) is not { } type)
            return NotModelled(call, "LAG or LEAD of a value of this type is not modelled.");
        var fits = (LiteralOf(parameters[2]) is ({ } literal, var negative) ? ScalarType.Fits(literal, type, negative) : null)
            ?? (!fallback.IsLiveJson && TypeOf(fallback, parameters[2]) is { } from && ScalarType.Converts(from, type));
        IReadOnlyList<IrNullReason> because = [.. value.NullableBecause, .. offsetBecause, .. itself ? [] : fallback.NullableBecause];
        return Typed(type with { Digits = null }, fits || itself ? because : ShapeJoin.Reasons(because, [Overflow(call)]), call);
    }
}
