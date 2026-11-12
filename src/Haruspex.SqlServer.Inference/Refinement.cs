using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Refinement: what a query's filters prove about the rows they keep. WHERE, an inner join's ON and HAVING keep
/// only rows for which their condition is TRUE, so a condition that cannot be TRUE when a column is NULL proves the column
/// non-null in every row kept. Which conditions those are is decided by three-valued logic (<see cref="Possible"/>), and
/// each engine rule it uses is observed in <c>RefinementObservationTests</c>. The README beside this file says why each
/// step is sound.
/// </summary>
internal sealed partial class ProcedureAnalyzer
{
    /// <summary>Whether the body was created with ANSI_NULLS off, under which <c>c = @x</c> is TRUE when both are null.</summary>
    private readonly bool ansiNullsOff;

    /// <summary>What the filters and branches being read prove (<see cref="Facts"/>): extended on entering one, restored on leaving.</summary>
    private Facts facts = Facts.None;

    /// <summary>A bound column's key among its source's columns: a table's column by name as declared, a relation's by position.</summary>
    private static string Key(BoundColumn bound) => "c:" + bound.Column.Name;

    private static string Key(BoundLocalColumn bound) => "#" + bound.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The queries whose aggregate's arguments are being read: a column read there is a row's, not a group's.</summary>
    private readonly List<QuerySpecification?> aggregating = [];

    /// <summary>Why a column read now may be NULL in a super-aggregate row (<see cref="Facts.Rolled"/>).</summary>
    private IrNullReason? Rolled(BoundSource source) => facts.Rolled(source, aggregating);

    /// <summary>Reads an aggregate's arguments of the query whose select list is being read.</summary>
    private T InAggregate<T>(Func<T> read)
    {
        aggregating.Add(current);
        try
        {
            return read();
        }
        finally
        {
            aggregating.RemoveAt(aggregating.Count - 1);
        }
    }

    /// <summary>Whether HAVING, or a branch read in its row, proves this aggregate of the query whose select list is being read non-null.</summary>
    private bool ProvedAggregate(FunctionCall call) => facts.Proved(current, Text(call));

    // ---- what a query's filters prove ---------------------------------------------------------------------------------

    /// <summary>What a query's filters prove of the rows it keeps, for reading its select list.</summary>
    private Facts.Found Filters(QuerySpecification query, BindingScope scope)
    {
        var found = new Facts.Found(Facts.Proof.WhereTheRowIsThere, query, scope.Sources)
        {
            Rolled = SuperAggregate(query) ? new IrNullReason(IrNullRule.SubtotalRow, At(query.GroupByClause).ToString()) : null,
        };

        // A query that aggregates without GROUP BY returns its one row even when WHERE and ON removed every row (observed:
        // An_aggregate_subquery_returns_its_row_even_when_its_where_rejects_the_outer_column). Its own columns are read only
        // inside its aggregates, over the rows kept, so what WHERE proves of them holds there; an enclosing query's column
        // is read as it is, so nothing WHERE or ON proves of it holds. HAVING filters the one row, so its facts hold.
        var own = MayAggregateWithoutGrouping(query) ? new HashSet<BoundSource>(found.Own, ReferenceEqualityComparer.Instance) : null;
        if (query.WhereClause?.SearchCondition is { } where)
            Refine(where, scope, found, rows: true, only: own);
        foreach (var reference in query.FromClause?.TableReferences ?? [])
            Joins(reference, scope, found, insideOptional: false, own);

        // HAVING keeps the groups for which it is TRUE: a grouped column or an aggregate it proves non-null is non-null in
        // every group kept (observed: Having_over_no_groups_returns_no_row_and_without_group_by_at_most_one).
        if (query.HavingClause?.SearchCondition is { } having)
        {
            Refine(having, scope, found, rows: true, only: null);
            foreach (var aggregate in Operands(having).OfType<FunctionCall>().Where(IsAggregate).Select(Text).Distinct(StringComparer.Ordinal))
            {
                if (!Possible(having, e => e is FunctionCall call && IsAggregate(call) && Text(call) == aggregate).HasFlag(Truth.True))
                    found.Aggregate(aggregate);
            }
        }

        return found;
    }

    /// <summary>
    /// The ON conditions of a FROM clause's joins. An inner join keeps the rows of its two inputs for which ON is TRUE: what
    /// it proves holds wherever those rows are, so also for a row matched there and then null-extended by an outer join
    /// around it, but not that the row is there. An outer join's ON never removes a row of its kept side (observed:
    /// A_condition_in_the_on_of_an_outer_join_never_drops_a_row_of_the_kept_side), so it proves only what holds of its
    /// optional side's row when it matched.
    /// </summary>
    /// <param name="own">When set, only these sources' columns are refined (see <see cref="Filters"/>).</param>
    private void Joins(TableReference reference, BindingScope scope, Facts.Found found, bool insideOptional, HashSet<BoundSource>? own)
    {
        switch (reference)
        {
            case QualifiedJoin { QualifiedJoinType: QualifiedJoinType.Inner } join:
                Joins(join.FirstTableReference, scope, found, insideOptional, own);
                Joins(join.SecondTableReference, scope, found, insideOptional, own);
                Refine(join.SearchCondition, scope, found, rows: !insideOptional, only: own);
                break;
            case QualifiedJoin { QualifiedJoinType: QualifiedJoinType.LeftOuter } join:
                // The optional side's sources are the query's own, so own changes nothing here.
                Joins(join.FirstTableReference, scope, found, insideOptional, own);
                Joins(join.SecondTableReference, scope, found, insideOptional: true, own);
                Refine(join.SearchCondition, scope, found, rows: false, only: Sources(join.SecondTableReference, scope));
                break;
            case QualifiedJoin { QualifiedJoinType: QualifiedJoinType.FullOuter } join:
                // Both sides are optional, and its ON proves nothing of either: a row of one side that fails it comes back
                // unmatched, with whatever it holds (observed: A_full_outer_joins_on_proves_nothing_of_either_side).
                Joins(join.FirstTableReference, scope, found, insideOptional: true, own);
                Joins(join.SecondTableReference, scope, found, insideOptional: true, own);
                break;
            case UnqualifiedJoin { UnqualifiedJoinType: UnqualifiedJoinType.CrossJoin } cross:
                Joins(cross.FirstTableReference, scope, found, insideOptional, own);
                Joins(cross.SecondTableReference, scope, found, insideOptional, own);
                break;
            case UnqualifiedJoin { UnqualifiedJoinType: UnqualifiedJoinType.CrossApply or UnqualifiedJoinType.OuterApply } apply:
                // An APPLY has no ON. What its right side's joins prove holds of its rows; under OUTER APPLY they may be
                // null-extended, as an outer join's optional side. What the applied query's own WHERE proves stays inside it
                // (its facts are its own, read while it is), so it never refines the outer columns it reads.
                Joins(apply.FirstTableReference, scope, found, insideOptional, own);
                Joins(apply.SecondTableReference, scope, found, insideOptional || apply.UnqualifiedJoinType == UnqualifiedJoinType.OuterApply, own);
                break;
            case JoinParenthesisTableReference parenthesis:
                Joins(parenthesis.Join, scope, found, insideOptional, own);
                break;
        }
    }

    /// <summary>
    /// Whether the query groups by ROLLUP, CUBE or GROUPING SETS (or WITH ROLLUP, WITH CUBE): anything but a list of
    /// expressions, or GROUP BY () alone. A composite grouping is counted too, which only claims less.
    /// </summary>
    private static bool SuperAggregate(QuerySpecification query) =>
        query.GroupByClause is { } group
        && (group.GroupByOption != GroupByOption.None
            || group.GroupingSpecifications.Any(s => s is not ExpressionGroupingSpecification
                && !(s is GrandTotalGroupingSpecification && group.GroupingSpecifications.Count == 1)));

    /// <summary>
    /// Whether the query may aggregate without GROUP BY, erring towards yes, since yes only claims less here: HAVING, or a
    /// built-in aggregate in its select list or ORDER BY (outside subqueries, wherever its arguments read), or a call of a
    /// function the project does not show to be a scalar function, which may be a CLR aggregate.
    /// </summary>
    private bool MayAggregateWithoutGrouping(QuerySpecification query)
    {
        if (query.GroupByClause is not null)
            return false;
        if (query.HavingClause is not null)
            return true;
        var finder = new AggregateFinder(call => FunctionSummaries.Callee(call) is { } callee
            && model.FindFunction(callee)?.Body.ReturnType is not ScalarFunctionReturnType);
        foreach (var element in query.SelectElements)
            element.Accept(finder);
        query.OrderByClause?.Accept(finder);
        return finder.Found.Count > 0;
    }

    /// <summary>The sources of this scope that a table reference binds.</summary>
    private static HashSet<BoundSource> Sources(TableReference reference, BindingScope scope)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(reference);
        var found = new HashSet<BoundSource>(ReferenceEqualityComparer.Instance);
        foreach (var source in scope.Sources.Where(s => s.Alias is not null && aliases.Contains(s.Alias)))
            found.Add(source);
        return found;

        void Collect(TableReference r)
        {
            switch (r)
            {
                case JoinTableReference join:
                    Collect(join.FirstTableReference);
                    Collect(join.SecondTableReference);
                    break;
                case JoinParenthesisTableReference parenthesis:
                    Collect(parenthesis.Join);
                    break;
                case NamedTableReference named:
                    aliases.Add(named.Alias?.Value ?? named.SchemaObject.BaseIdentifier.Value);
                    break;
                case TableReferenceWithAlias { Alias: { } alias }:
                    aliases.Add(alias.Value);
                    break;
            }
        }
    }

    /// <summary>
    /// Records every column that <paramref name="condition"/> proves non-null in the rows it keeps: those for which it is
    /// never TRUE when the column is NULL. With <paramref name="rows"/>, the column's source's row is proved there too: an
    /// outer join's null-extended row has every column NULL, so it is never kept. With <paramref name="only"/>, only
    /// columns of those sources are recorded.
    /// </summary>
    private void Refine(BooleanExpression condition, BindingScope scope, Facts.Found found, bool rows, HashSet<BoundSource>? only)
    {
        var bindings = new Dictionary<ColumnReferenceExpression, (BoundSource Source, string Key)?>(ReferenceEqualityComparer.Instance);
        (BoundSource Source, string Key)? Bind(ColumnReferenceExpression column)
        {
            if (!bindings.TryGetValue(column, out var bound))
            {
                bound = binder.BindColumn(column, scope) switch
                {
                    BoundColumn c => (c.Source, Key(c)),
                    BoundLocalColumn { Ordinal: >= 0 } l => (l.Source, Key(l)),
                    _ => null,
                };
                bindings[column] = bound;
            }

            return bound;
        }

        var candidates = Operands(condition).OfType<ColumnReferenceExpression>().Select(Bind).OfType<(BoundSource Source, string Key)>()
            .Distinct().ToList();
        foreach (var (source, key) in candidates)
        {
            if (only is not null && !only.Contains(source))
                continue;
            var possible = Possible(condition, e => e is ColumnReferenceExpression column && Bind(column) is { } b && ReferenceEquals(b.Source, source) && b.Key == key);
            if (possible.HasFlag(Truth.True))
                continue;
            found.Value(source, key);
            if (rows)
                found.Row(source);
        }
    }

    // ---- three-valued logic ---------------------------------------------------------------------------------------

    /// <summary>A set of truth values: what a condition can be.</summary>
    [Flags]
    private enum Truth
    {
        None = 0,
        True = 1,
        False = 2,
        Unknown = 4,
        Any = True | False | Unknown,
    }

    /// <summary>
    /// What <paramref name="condition"/> can be when the value <paramref name="target"/> recognises is NULL, whatever every
    /// other value is. Only a comparison, LIKE, BETWEEN or IN with the value itself as an operand is UNKNOWN for it (observed:
    /// A_comparison_with_a_null_column_is_unknown_and_keeps_no_row, Like_is_unknown_when_the_value_or_the_pattern_is_null);
    /// <c>IS NULL</c> is TRUE and <c>IS NOT NULL</c> FALSE; anything else, the value inside ISNULL, COALESCE, CASE, an
    /// expression or a subquery included, can be anything (observed:
    /// A_null_inside_isnull_coalesce_or_case_can_make_the_predicate_true). AND, OR and NOT combine the sets by SQL's
    /// three-valued logic (observed: Not_of_an_unknown_comparison_is_unknown_and_keeps_no_null,
    /// Or_keeps_a_null_when_any_disjunct_can_be_true); treating their operands as independent can only add values.
    /// </summary>
    private Truth Possible(BooleanExpression condition, Func<ScalarExpression, bool> target)
    {
        bool Is(ScalarExpression? e) => e is not null && target(Strip(e));

        switch (condition)
        {
            case BooleanParenthesisExpression parenthesis:
                return Possible(parenthesis.Expression, target);
            case BooleanNotExpression not:
                return Not(Possible(not.Expression, target));
            case BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.And } and:
                return Combine(Possible(and.FirstExpression, target), Possible(and.SecondExpression, target), And);
            case BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.Or } or:
                return Combine(Possible(or.FirstExpression, target), Possible(or.SecondExpression, target), Or);
            case BooleanIsNullExpression isNull when Is(isNull.Expression):
                return isNull.IsNot ? Truth.False : Truth.True;
            case BooleanComparisonExpression comparison when Is(comparison.FirstExpression) || Is(comparison.SecondExpression):
                return Comparison(comparison.ComparisonType, Is(comparison.FirstExpression) ? comparison.SecondExpression : comparison.FirstExpression);
            case LikePredicate like when Is(like.FirstExpression) || Is(like.SecondExpression):
                return like.NotDefined ? Not(Truth.Unknown) : Truth.Unknown;
            case BooleanTernaryExpression between:
            {
                // x BETWEEN a AND b is x >= a AND x <= b; NOT BETWEEN is its negation.
                var low = Is(between.FirstExpression) || Is(between.SecondExpression) ? Truth.Unknown : Truth.Any;
                var high = Is(between.FirstExpression) || Is(between.ThirdExpression) ? Truth.Unknown : Truth.Any;
                var result = Combine(low, high, And);
                return between.TernaryExpressionType == BooleanTernaryExpressionType.NotBetween ? Not(result) : result;
            }

            case InPredicate @in:
            {
                // x IN (a, b) is x = a OR x = b. x IN (subquery) is FALSE when the subquery returns no rows, even for a
                // null x, and UNKNOWN otherwise (observed: In_a_subquery_with_no_rows_is_false_and_not_in_it_is_true_even_for_a_null).
                Truth result;
                if (@in.Subquery is not null)
                    result = Is(@in.Expression) ? Truth.False | Truth.Unknown : Truth.Any;
                else if (Is(@in.Expression))
                    result = @in.Values.Select(v => Comparison(BooleanComparisonType.Equals, v)).Aggregate((a, b) => Combine(a, b, Or));
                else
                    result = @in.Values.Select(v => Is(v) ? Truth.Unknown : Truth.Any).Aggregate((a, b) => Combine(a, b, Or));
                return @in.NotDefined ? Not(result) : result;
            }

            default:
                return Truth.Any;
        }
    }

    /// <summary>
    /// A comparison of the null value with <paramref name="other"/>: UNKNOWN. With ANSI_NULLS off when the body was created,
    /// <c>=</c> and <c>&lt;&gt;</c> against anything but a column are TRUE or FALSE instead (observed:
    /// A_procedure_created_with_ansi_nulls_off_keeps_a_null_that_equals_a_null_variable); a comparison of two columns is not
    /// affected.
    /// </summary>
    private Truth Comparison(BooleanComparisonType type, ScalarExpression other)
    {
        if (!ansiNullsOff || Strip(other) is ColumnReferenceExpression)
            return Truth.Unknown;
        return type is BooleanComparisonType.Equals or BooleanComparisonType.NotEqualToBrackets or BooleanComparisonType.NotEqualToExclamation
            ? Truth.Any
            : Truth.Unknown;
    }

    private static Truth Not(Truth a) =>
        (a.HasFlag(Truth.True) ? Truth.False : Truth.None) | (a.HasFlag(Truth.False) ? Truth.True : Truth.None) | (a & Truth.Unknown);

    private static Truth And(Truth a, Truth b) =>
        a == Truth.False || b == Truth.False ? Truth.False : a == Truth.True && b == Truth.True ? Truth.True : Truth.Unknown;

    private static Truth Or(Truth a, Truth b) =>
        a == Truth.True || b == Truth.True ? Truth.True : a == Truth.False && b == Truth.False ? Truth.False : Truth.Unknown;

    /// <summary>Every value <paramref name="op"/> gives for some value of each set.</summary>
    private static Truth Combine(Truth a, Truth b, Func<Truth, Truth, Truth> op)
    {
        var result = Truth.None;
        foreach (var x in new[] { Truth.True, Truth.False, Truth.Unknown }.Where(x => a.HasFlag(x)))
        {
            foreach (var y in new[] { Truth.True, Truth.False, Truth.Unknown }.Where(y => b.HasFlag(y)))
                result |= op(x, y);
        }

        return result;
    }

    private static ScalarExpression Strip(ScalarExpression e)
    {
        while (e is ParenthesisExpression p)
            e = p.Expression;
        return e;
    }

    /// <summary>
    /// The direct operands of a condition's comparisons, LIKE, BETWEEN, IN and IS NULL, through AND, OR, NOT and
    /// parentheses, without parentheses: the only values <see cref="Possible"/> can find UNKNOWN or FALSE for.
    /// </summary>
    private static IEnumerable<ScalarExpression> Operands(BooleanExpression condition)
    {
        var found = condition switch
        {
            BooleanParenthesisExpression p => Operands(p.Expression),
            BooleanNotExpression n => Operands(n.Expression),
            BooleanBinaryExpression b => Operands(b.FirstExpression).Concat(Operands(b.SecondExpression)),
            BooleanIsNullExpression n => [n.Expression],
            BooleanComparisonExpression c => [c.FirstExpression, c.SecondExpression],
            LikePredicate l => [l.FirstExpression, l.SecondExpression],
            BooleanTernaryExpression t => [t.FirstExpression, t.SecondExpression, t.ThirdExpression],
            InPredicate i => [i.Expression, .. i.Values],
            _ => [],
        };
        return found.OfType<ScalarExpression>().Select(Strip);
    }

    // ---- built-in aggregates ----------------------------------------------------------------------------------------

    /// <summary>
    /// Built-in aggregates, which make a query without GROUP BY return exactly one row. A user's function is always called
    /// with its schema, so a name without one is the built-in. The JSON aggregates are among them.
    /// </summary>
    private static readonly HashSet<string> Aggregates = new(StringComparer.OrdinalIgnoreCase)
    {
        "count", "count_big", "sum", "avg", "min", "max", "stdev", "stdevp", "var", "varp", "string_agg", "checksum_agg",
        "grouping", "grouping_id", "approx_count_distinct", "json_arrayagg", "json_objectagg",
    };

    /// <summary>A call of a built-in aggregate as an aggregate: with a window (OVER) it is computed per row instead.</summary>
    private static bool IsAggregate(FunctionCall call) =>
        call is { CallTarget: null, OverClause: null } && Aggregates.Contains(call.FunctionName.Value);

    /// <summary>
    /// COUNT, COUNT_BIG, MIN and MAX. COUNT and COUNT_BIG are 0 over no rows and never NULL, except that COUNT overflows
    /// int to NULL under a caller's ANSI_WARNINGS and ARITHABORT off (observed:
    /// Count_over_more_rows_than_int_holds_is_null_under_the_callers_ansi_warnings_and_arithabort_off). MIN and MAX return
    /// one of their argument's values, so its type and facets, and are NULL over no rows (without GROUP BY) and when every
    /// value is (observed: An_aggregate_without_group_by_returns_exactly_one_row_even_over_no_rows,
    /// An_aggregate_with_group_by_returns_no_row_over_no_rows). SUM, AVG and the statistics change the type; the others are not
    /// modelled yet.
    /// </summary>
    private Value BuiltInAggregate(FunctionCall call, BindingScope scope)
    {
        var at = At(call);
#pragma warning disable CA1308 // Built-in function names are ASCII, so lower case matches them exactly, and the cases are written as T-SQL usually is.
        var name = call.FunctionName.Value.ToLowerInvariant();
#pragma warning restore CA1308
        if (name is "json_arrayagg" or "json_objectagg")
            return Constructor(call, scope);

        switch (name)
        {
            case "count" or "count_big" when call.Parameters.Count == 1:
            {
                // COUNT is NULL past int's range under a caller's options, which a procedure can't prevent
                // (docs/decisions/0014-count-is-nullable.md); COUNT_BIG can't overflow.
                var type = ScalarType.Of(name == "count" ? BuiltInType.Int : BuiltInType.BigInt);
                EquatableArray<IrNullReason> because = name == "count" && !ProvedAggregate(call) ? [new IrNullReason(IrNullRule.CountOverflow, at.ToString())] : [];
                return Value.Computed(SqlTypes.ToConvertedShape(type, at), because);
            }

            case "min" or "max" when call.Parameters.Count == 1:
            {
                var argument = InAggregate(() => Expression(call.Parameters[0], scope));
                if (argument.Shape is UnknownShape unknown)
                    return Value.Unknown(unknown);
                if (argument.IsLiveJson || argument.Shape is not ScalarShape)
                    return Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"{name.ToUpperInvariant()} of a value that is not a scalar is not modelled: {Text(call)}", at));

                // Refused when the procedure is created (8117; observed: Min_and_max_of_a_bit_are_refused_with_or_without_a_window).
                if (TypeOf(argument) is { IsBit: true })
                    return NotModelled(call, $"SQL Server refuses {name.ToUpperInvariant()} of bit.");
                return ProvedAggregate(call)
                    ? argument.WithNullableBecause([])
                    : argument.WithNullableBecause([.. argument.NullableBecause, .. Aggregate(call)]);
            }

            case "sum" when call.Parameters.Count == 1:
                return Sum(call, scope);

            case "avg" or "stdev" or "stdevp" or "var" or "varp" when call.Parameters.Count == 1:
                return Average(call, scope, name);

            case "string_agg" when call.Parameters.Count == 2:
                return StringAggregate(call, scope);

            default:
                return Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"The aggregate {Text(call)} is not modelled yet.", at));
        }
    }
}
