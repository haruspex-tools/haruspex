using Haruspex.Collections;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Inference.Typing;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// Window functions, a call with OVER. README.md in this folder ("Window functions") gives the rules and the
// observations they rest on (FunctionObservationTests in the differential tests).
internal sealed partial class Typer
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
    private Value Window(FunctionCall call, ReadScope scope, BuiltInFunction function, WindowRule rule)
    {
        var name = function.Name;
        // Never null: the function table reads a call as a window only when it has OVER (FunctionTable).
        var over = call.OverClause!;

        // OVER w takes the partition, order and frame of w's definition in the query's WINDOW clause, whose
        // frame may be empty (A_window_named_in_a_window_clause_brings_its_frame_to_every_function_over_it). Only the OVER
        // clause is read here, so a function over a name is unknown.
        if (over.WindowName is not null)
            return NotModelled(call, "a window named in a WINDOW clause is not modelled yet.");
        EquatableArray<IrNullReason> Framed() => rule.EmptyFrame && MayBeEmpty(over) ? [new IrNullReason(IrNullRule.EmptyFrame, At(call).ToString())] : [];
        EquatableArray<IrNullReason> Sample() => rule.TooFewValues ? [new IrNullReason(IrNullRule.TooFewValues, At(call).ToString())] : [];

        switch (rule.Result)
        {
            case WindowResult.Fixed:
                // Never null: a fixed result's rule names its type (the window rules of the function table).
                return Typed(ScalarType.Of(rule.Type!), rule.CountOverflow ? CountPastInt(call) : [], call);

            case WindowResult.Sum:
            {
                var argument = Expression(call.Parameters[0], scope);
                if (argument.Shape is UnknownShape)
                    return argument;
                if (argument.IsLiveJson || TypeOf(argument) is not { } from || SumType(from) is not { } type)
                    return NotModelled(call, "SUM of a value of this type is not modelled yet.");
                EquatableArray<IrNullReason> because = [.. argument.NullableBecause, .. from is { IsDecimal: true, Precision: <= 19 } ? [] : Overflow(call), .. Framed()];
                return Typed(type, because, call);
            }

            // AVG and the statistics as without a window (Average): a partition is never empty, but a frame may be, and a
            // sample statistic over a frame of one value is NULL.
            case WindowResult.Statistic:
            {
                var argument = Expression(call.Parameters[0], scope);
                if (argument.Shape is UnknownShape)
                    return argument;
                if (argument.IsLiveJson || TypeOf(argument) is not { } from || Statistic(rule.Average, from) is not var (type, overflows))
                    return NotModelled(call, $"{name.ToUpperInvariant()} of a value of this type is not modelled.");
                EquatableArray<IrNullReason> because =
                [
                    .. argument.NullableBecause,
                    .. Sample(),
                    .. overflows ? Overflow(call) : [],
                    .. AverageRounds(rule.Average, from) ? PrecisionLoss(call) : [],
                    .. Framed(),
                ];
                return Typed(type, because, call);
            }

            case WindowResult.Argument:
            {
                var argument = Expression(call.Parameters[0], scope);
                if (argument.Shape is UnknownShape)
                    return argument;
                if (argument.IsLiveJson || argument.Shape is not ScalarShape)
                    return NotModelled(call, "a window over a value that is not a scalar is not modelled.");

                // Refused when the procedure is created (8117; observed: Min_and_max_of_a_bit_are_refused_with_or_without_a_window).
                if (rule.RefusesBit && TypeOf(argument) is { IsBit: true })
                    return NotModelled(call, $"SQL Server refuses {name.ToUpperInvariant()} of bit.");
                return argument.WithNullableBecause([.. argument.NullableBecause, .. Framed()]);
            }

            case WindowResult.Offset:
                return Offset(call, scope);

            default:
            {
                if (call.WithinGroupClause?.OrderByClause.OrderByElements is not [var element])
                    return NotModelled(call, "this window function is not modelled yet.");

                // Ordered by a constant, it is refused when the procedure is created (5308, 5309; observed:
                // Json_value_eomonth_string_agg_and_percentiles_refuse_arguments_they_cannot_read).
                if (LiteralOf(element.Expression) is not null)
                    return NotModelled(call, "SQL Server refuses a percentile ordered by a constant.");
                var argument = Expression(element.Expression, scope);
                if (argument.Shape is UnknownShape)
                    return argument;
                if (argument.IsLiveJson || argument.Shape is not ScalarShape)
                    return NotModelled(call, "a percentile of a value that is not a scalar is not modelled.");
                if (rule.Result == WindowResult.PercentileOfValues)
                    return argument;
                return TypeOf(argument) is { IsNumber: true } or { IsBit: true }
                    ? Typed(ScalarType.Of(BuiltInType.Float), argument.NullableBecause, call)
                    : NotModelled(call, "PERCENTILE_CONT of a value of this type is not modelled.");
            }
        }
    }

    /// <summary>A call with OVER of a function that is no window function inference reads.</summary>
    private Value WindowNotModelled(FunctionCall call) => NotModelled(call, "this window function is not modelled yet.");

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
    /// or NULL under a caller's ANSI_WARNINGS and ARITHABORT off; a decimal rounded to x's fewer places is NULL under
    /// NUMERIC_ROUNDABORT on as well (observed in PrecisionLossObservationTests). A NULL offset is NULL; offset 0 is the row itself (observed:
    /// Lag_and_lead_are_null_past_the_partitions_edge_unless_a_default_is_given_which_is_converted_to_the_columns_type).
    /// Without a default it gives x's values only, so x's facts; with one, which may be outside them, only the type's.
    /// </summary>
    private Value Offset(FunctionCall call, ReadScope scope)
    {
        var parameters = call.Parameters;
        var value = Expression(parameters[0], scope);
        if (value.Shape is UnknownShape)
            return value;
        if (value.IsLiveJson || value.Shape is not ScalarShape)
            return NotModelled(call, "LAG or LEAD of a value that is not a scalar is not modelled.");

        EquatableArray<IrNullReason> offsetBecause = [];
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
            EquatableArray<IrNullReason> edge = itself ? [] : [new IrNullReason(IrNullRule.WindowEdge, At(call).ToString())];
            return value.WithNullableBecause([.. value.NullableBecause, .. edge, .. offsetBecause]);
        }

        var fallback = Expression(parameters[2], scope);
        if (fallback.Shape is UnknownShape)
            return fallback;
        if (TypeOf(value) is not { } type)
            return NotModelled(call, "LAG or LEAD of a value of this type is not modelled.");
        var fits = (LiteralOf(parameters[2]) is ({ } literal, var negative) ? Conversions.Fits(literal, type, negative) : null)
            ?? (!fallback.IsLiveJson && OperandOf(fallback, parameters[2]) is { } from && Conversions.Converts(from, type));
        EquatableArray<IrNullReason> because = [.. value.NullableBecause, .. offsetBecause, .. itself ? [] : fallback.NullableBecause];
        if (itself)
            return Typed(type, because, call);
        return Typed(type, NullReasons.Join(because, ConversionReasons(summaries.Session, fallback, parameters[2], type, fits, At(call))), call);
    }
}
