using System.Globalization;
using Haruspex.Collections;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Inference.Typing;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// CASE, IIF, CHOOSE and NULLIF, and COALESCE and ISNULL over JSON. README.md in this folder ("CASE, IIF, CHOOSE and
// NULLIF", "COALESCE and ISNULL over JSON") gives the rules and the observations they rest on (CaseObservationTests in the
// differential tests).
internal sealed partial class Typer
{
    // ---- what a branch knows --------------------------------------------------------------------------------------------

    /// <summary>A variable or parameter, never null in a branch that proved it so.</summary>
    private static Value Branched(VariableReference variable, Value value, ReadScope scope) =>
        value.Shape is not UnknownShape && scope.Facts.Proved(Binder.Resolve(variable, scope.Names).Name) ? value.WithNullableBecause([]) : value;

    /// <summary>
    /// What a branch knows: <paramref name="held"/> was TRUE (none for an ELSE) and each of <paramref name="failed"/> was FALSE
    /// or UNKNOWN. A value is proved non-null when, were it NULL, no condition could take the truth value it took
    /// (<see cref="Possible"/>, the three-valued logic of refinement). Only columns, variables and the query's aggregates are
    /// proved: each is the same value wherever it is read in the row. A subquery or a call may give another value when read
    /// again (Coalesce_reads_a_subquery_twice…), so testing one proves nothing about it.
    /// </summary>
    private Facts.Found Branch(IReadOnlyList<BooleanExpression> failed, BooleanExpression? held, ReadScope scope)
    {
        var found = new Facts.Found(Facts.Proof.OfTheValue, scope.Query, []);
        if (held is not null)
            Prove(held, Truth.True, scope, found);
        foreach (var condition in failed)
            Prove(condition, Truth.False | Truth.Unknown, scope, found);
        return found;
    }

    private void Prove(BooleanExpression condition, Truth took, ReadScope scope, Facts.Found found)
    {
        bool Impossible(Func<ScalarExpression, bool> target) => (Possible(condition, target) & took) == Truth.None;

        foreach (var operand in Operands(condition))
        {
            switch (operand)
            {
                case ColumnReferenceExpression { ColumnType: ColumnType.Regular } column when Bound(column, scope) is var (source, key):
                    if (Impossible(e => e is ColumnReferenceExpression c && Bound(c, scope) is var (s, k) && ReferenceEquals(s, source) && k == key))
                    {
                        found.Value(source, key);
                        found.Row(source);
                    }

                    break;
                case VariableReference variable:
                    if (Binder.Resolve(variable, scope.Names) is var proved && Impossible(e => e is VariableReference v && Binder.Resolve(v, scope.Names) == proved))
                        found.Variable(proved.Name);
                    break;
                case FunctionCall call when IsAggregate(call) && scope.Query is not null:
                    var aggregate = AggregateKey.Of(call);
                    if (Impossible(e => e is FunctionCall c && IsAggregate(c) && AggregateKey.Of(c) == aggregate))
                        found.Aggregate(aggregate);
                    break;
            }
        }
    }

    private (BoundSource Source, ColumnKey Key)? Bound(ColumnReferenceExpression column, ReadScope scope) => binder.Resolve(column, scope.Bound) switch
    {
        TableColumn c => (c.Source, ColumnKey.Of(c)),
        RelationColumn { Ordinal: >= 0 } l => (l.Source, ColumnKey.Of(l)),
        _ => null,
    };

    // ---- CASE and IIF ---------------------------------------------------------------------------------------------------

    /// <summary>A searched CASE: each WHEN's condition, its result, and the ELSE.</summary>
    private Value Case(SearchedCaseExpression expression, ReadScope scope) =>
        Branches(expression, [.. expression.WhenClauses.Select(w => (w.WhenExpression, w.ThenExpression))], expression.ElseExpression, scope);

    /// <summary>
    /// A simple CASE, <c>CASE x WHEN 1 THEN …</c>, compares with <c>=</c> (observed: WHEN NULL never matches), so each WHEN is
    /// read as <c>x = 1</c>.
    /// </summary>
    private Value Case(SimpleCaseExpression expression, ReadScope scope) =>
        Branches(
            expression,
            [.. expression.WhenClauses.Select(w => ((BooleanExpression)new BooleanComparisonExpression
            {
                ComparisonType = BooleanComparisonType.Equals,
                FirstExpression = expression.InputExpression,
                SecondExpression = w.WhenExpression,
            }, w.ThenExpression))],
            expression.ElseExpression,
            scope);

    /// <summary><c>IIF(c, a, b)</c> is <c>CASE WHEN c THEN a ELSE b END</c> (observed: the same types).</summary>
    private Value Iif(IIfCall call, ReadScope scope) =>
        Branches(call, [(call.Predicate, call.ThenExpression)], call.ElseExpression, scope);

    /// <summary>
    /// The branches of a CASE: each read knowing what held (<see cref="Branch"/>). Its type is the precedence over its results
    /// (<see cref="Results(TSqlFragment, List{ValueTuple{ScalarExpression, Value}}, EquatableArray{IrNullReason}, ReadScope)"/>); without ELSE it is NULL when no WHEN holds (<c>no-branch-matched</c>).
    /// </summary>
    private Value Branches(TSqlFragment at, IReadOnlyList<(BooleanExpression When, ScalarExpression Then)> whens, ScalarExpression? otherwise, ReadScope scope)
    {
        var results = new List<(ScalarExpression Expression, Value? Value)>();
        var failed = new List<BooleanExpression>();
        foreach (var (when, then) in whens)
        {
            results.Add((then, Strip(then) is NullLiteral ? null : Expression(then, scope.Branching(Branch(failed, when, scope)))));
            failed.Add(when);
        }

        if (otherwise is not null)
            results.Add((otherwise, Strip(otherwise) is NullLiteral ? null : Expression(otherwise, scope.Branching(Branch(failed, null, scope)))));
        return Results(at, results, otherwise is null ? [new IrNullReason(IrNullRule.NoBranchMatched, At(at).ToString())] : [], scope);
    }

    /// <summary>
    /// The value of a CASE, IIF or CHOOSE from its results (a null value for the NULL literal): the type of highest precedence
    /// over them, as COALESCE's (<see cref="TypeChain.Of"/>; observed:
    /// Case_iif_and_choose_take_the_type_of_highest_precedence_over_their_results); null when a result may be, or is the NULL
    /// literal (<c>null-literal</c>), or converting one to the type may overflow (<c>arithmetic-overflow</c>), or for
    /// <paramref name="also"/>. When
    /// every other result is a string literal, or every one a number, it is one of them exactly: a literal is varchar or
    /// nvarchar, never padded, so trailing spaces are its own (observed:
    /// A_literal_result_keeps_its_trailing_spaces_and_a_fixed_length_result_pads). Live JSON is not modelled: whether a CASE
    /// over it nests as JSON depends on whether the server folds it to one branch (observed:
    /// Case_iif_choose_and_coalesce_over_json_are_text_unless_the_server_folds_them_to_one_branch). Results that are all JSON
    /// text are JSON text of their join, and under json_query, where folding does not matter, so are any.
    /// </summary>
    private Value Results(TSqlFragment at, List<(ScalarExpression Expression, Value? Value)> results, EquatableArray<IrNullReason> also, ReadScope scope)
    {
        var given = new List<(ScalarExpression Expression, Value Value)>();
        foreach (var (expression, result) in results)
        {
            if (result is not null)
                given.Add((expression, result));
        }

        if (given.Count == 0)
            return NotModelled(at, "every result is NULL.");
        if (given.FirstOrDefault(r => r.Value.Shape is UnknownShape) is { Value: { } unknown })
            return unknown;

        // Under json_query whether the server folds it no longer matters: json_query makes either result JSON, so what it holds
        // is the join of its results' JSON, literals parsed. Elsewhere, JSON text in every result is JSON text.
        var underJsonQuery = ReferenceEquals(at, scope.JsonQueryArgument);
        if (given.Any(r => r.Value.IsLiveJson) && !underJsonQuery)
            return NotModelled(at, "whether it nests as JSON depends on whether the server folds it to one result.");
        if (underJsonQuery || given.Any(r => r.Value.Shape is JsonTextShape))
        {
            var (json, content) = JsonContent(given.Select(r => (r.Expression, r.Value)));
            if (json)
            {
                var nulls = given.Select(r => r.Value.NullableBecause)
                    .Concat(results.Where(r => r.Value is null).Select(r => (EquatableArray<IrNullReason>)[new IrNullReason(IrNullRule.NullLiteral, At(r.Expression).ToString())]))
                    .Append(also)
                    .Aggregate((EquatableArray<IrNullReason>)[], NullReasons.Join);
                return new Value(new JsonTextShape(content), nulls);
            }
        }

        var where = At(at);
        var combined = Chained([.. given.Select(r => (r.Value, r.Expression, where))]);
        if (combined.Type is not { } type)
            return NotModelled(at, combined.Why!); // Never null: a chain without a type says why (Chained).

        var because = given.Select(r => r.Value.NullableBecause)
            .Concat(combined.Overflows)
            .Concat(results.Where(r => r.Value is null).Select(r => (EquatableArray<IrNullReason>)[new IrNullReason(IrNullRule.NullLiteral, At(r.Expression).ToString())]))
            .Append(also)
            .Aggregate((EquatableArray<IrNullReason>)[], NullReasons.Join);
        // A CASE whose condition is constant is folded to one result, an int literal then counting its own digits (observed:
        // An_int_constant_counts_its_own_digits_beside_a_decimal); which, is not known here.
        var value = Typed(type, because, at) with { Digits = type.Type == BuiltInType.Int ? IntDigits.Foldable(given.Select(r => r.Value.Digits)) : IntDigits.OfType };
        return LiteralSet(given.Select(r => r.Expression), type, at) is { } set && value.Shape is ScalarShape scalar
            ? value with { Shape = scalar with { Type = scalar.Type with { Facets = (scalar.Type.Facets ?? new IrFacets()) with { Values = set } } }, Digits = value.Digits }
            : value;
    }

    /// <summary>
    /// The exact set of literal results, when every result is one: strings for a string type, numbers for an exact number. A
    /// literal converts to the type without changing (the type is wide enough for each, and an ASCII varchar literal is the
    /// same in nvarchar). Items are in a stable order, each once.
    /// </summary>
    private IrValueSet? LiteralSet(IEnumerable<ScalarExpression> results, ScalarType type, TSqlFragment at)
    {
        var all = results.Select(LiteralOf).ToList();
        if (all.Any(l => l is null))
            return null;
        var literals = all.OfType<(Literal Literal, bool Negative)>().ToList();
        IReadOnlyList<IrValue> items;
        if (type.IsString && literals.All(l => l.Literal is StringLiteral))
        {
            // A string longer than the type is cut short (a national type stops at 4000 characters).
            if (!type.IsMax && literals.Any(l => l.Literal.Value.Length > type.Length))
                return null;
            items = [.. literals.Select(l => l.Literal.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(s => new IrValue(s, IsString: true))];
        }
        else if ((type.IsInteger || type.IsDecimal) && literals.All(l => l.Literal is IntegerLiteral or NumericLiteral))
        {
            var numbers = new List<ExactDecimal>();
            foreach (var (literal, negative) in literals)
            {
                if (!ExactDecimal.TryParse(literal.Value, out var number))
                    return null;
                number = negative ? number.Negate() : number;

                // Past 38 digits a decimal's scale is cut and a literal with more places is rounded (observed:
                // Exact_numbers_past_38_digits_keep_their_integer_digits_and_cut_the_scale), so it is not one of its values.
                if (type.IsDecimal && (number.Scale > type.Scale || number.IntegerDigits > type.Precision - type.Scale))
                    return null;
                if (!numbers.Any(n => n.CompareTo(number) == 0))
                    numbers.Add(number);
            }

            items = [.. numbers.Order().Select(n => new IrValue(n.ToString(), IsString: false))];
        }
        else
        {
            return null;
        }

        return new IrValueSet([.. items], IrValueMatch.Exact, [new IrFactSource(IrFactSourceKind.Expression, At(at).ToString())]);
    }

    // ---- CHOOSE ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>CHOOSE(i, a, b, …)</c>: the precedence over its values, as CASE (<see cref="Results(TSqlFragment, List{ValueTuple{ScalarExpression, Value}}, EquatableArray{IrNullReason}, ReadScope)"/>). NULL when i is, or is below 1
    /// or above the count (<c>out-of-range</c>; observed: Case_without_else_choose_out_of_range_and_nullif_of_equal_values_are_null),
    /// unless i is an integer literal in range or an integer whose bounds (a CHECK's) keep it in range. Its values are not read
    /// as branches: which one is taken is a number, not a condition.
    /// </summary>
    private Value Choose(FunctionCall call, ReadScope scope)
    {
        var count = call.Parameters.Count - 1;
        var index = Expression(call.Parameters[0], scope);
        var results = call.Parameters.Skip(1).Select(p => (p, Strip(p) is NullLiteral ? null : Expression(p, scope))).ToList();
        var because = InRange(index, call.Parameters[0], count)
            ? index.NullableBecause
            : NullReasons.Join(index.NullableBecause, [new IrNullReason(IrNullRule.OutOfRange, At(call).ToString())]);
        return Results(call, results, because, scope);
    }

    private static bool InRange(Value index, ScalarExpression expression, int count)
    {
        if (LiteralOf(expression) is ({ } literal and IntegerLiteral, var negative))
            return !negative && long.TryParse(literal.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= count;
        if (index.Shape is not ScalarShape { Type.Facets: { Min: { } min, Max: { } max }, BuiltIn.IsInteger: true })
            return false;
        if (!ExactDecimal.TryParse(min.Value, out var low) || !ExactDecimal.TryParse(max.Value, out var high))
            return false;

        // An integer above an exclusive bound is at least the next one: > 0 is >= 1.
        var one = ExactDecimal.Parse("1");
        var ceiling = ExactDecimal.Parse(count.ToString(CultureInfo.InvariantCulture));
        return (min.Inclusive ? low.CompareTo(one) >= 0 : low.CompareTo(ExactDecimal.Parse("0")) >= 0)
            && (max.Inclusive ? high.CompareTo(ceiling) <= 0 : high.CompareTo(ExactDecimal.Parse((count + 1).ToString(CultureInfo.InvariantCulture))) <= 0);
    }

    // ---- NULLIF ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>NULLIF(a, b)</c>: a's value, unchanged, or NULL when a and b compare equal (<c>null-when-equal</c>), so a's type and facts; never
    /// equal when b is the NULL literal. An integer literal a is the smallest integer type that holds it (observed:
    /// Nullif_is_its_first_arguments_type_but_an_integer_literal_is_the_smallest_integer_holding_it). Over JSON it is text
    /// unless the server folds it (observed, as for CASE), which is not modelled.
    /// </summary>
    private Value NullIf(NullIfExpression nullIf, ReadScope scope)
    {
        if (Strip(nullIf.FirstExpression) is NullLiteral)
            return NotModelled(nullIf, "NULLIF of the NULL literal is an error.");
        var value = Expression(nullIf.FirstExpression, scope);
        if (Strip(nullIf.SecondExpression) is NullLiteral || value.Shape is UnknownShape)
            return value;
        if (value.IsLiveJson || value.Shape is JsonTextShape)
            return NotModelled(nullIf, "whether NULLIF over JSON nests as JSON depends on whether the server folds it.");

        var because = NullReasons.Join(value.NullableBecause, [new IrNullReason(IrNullRule.NullWhenEqual, At(nullIf).ToString())]);
        if (LiteralOf(nullIf.FirstExpression) is ({ } literal and IntegerLiteral, var negative)
            && long.TryParse(literal.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
        {
            n = negative ? -n : n;
            var smallest = n is >= byte.MinValue and <= byte.MaxValue ? BuiltInType.TinyInt : n is >= short.MinValue and <= short.MaxValue ? BuiltInType.SmallInt : BuiltInType.Int;
            return Typed(ScalarType.Of(smallest), because, nullIf);
        }

        return value with { NullableBecause = because, Digits = IntDigits.Foldable([value.Digits]) };
    }

    // ---- COALESCE and ISNULL over JSON ----------------------------------------------------------------------------------

    private static bool IsJson(Value value) => value.IsLiveJson || value.Shape is JsonTextShape;

    /// <summary>
    /// What a value built from <paramref name="parts"/> holds as JSON, each written as it is: a live value its shape, JSON text its
    /// content, and a literal that is a JSON document its parsed shape (<see cref="JsonLiterals"/>), not only
    /// <c>'[]'</c> and <c>'{}'</c>. <c>NotJson</c> when a part may not be JSON (a column, a literal that is not a
    /// document); otherwise JSON, its content null when not known (a JSON text of unknown content, or <c>'null'</c>, which is
    /// JSON but not a document). The content is the join of the parts' (<see cref="JsonShapes.Join"/>): <c>'[]'</c> lets
    /// arrays have no items, and shapes that do not join are a union.
    /// </summary>
    private (bool Json, Shape? Content) JsonContent(IEnumerable<(ScalarExpression Expression, Value Value)> parts)
    {
        Shape? content = null;
        var known = true;
        foreach (var (expression, value) in parts)
        {
            Shape shape;
            if (value.IsLiveJson)
            {
                shape = value.Shape;
            }
            else if (value.Shape is JsonTextShape text)
            {
                if (text.Content is null)
                {
                    known = false;
                    continue;
                }

                shape = text.Content;
            }
            else if (LiteralOf(expression) is ({ } literal and StringLiteral, _))
            {
                if (literal.Value.Trim(' ', '\t', '\r', '\n') == "null")
                {
                    known = false;
                    continue;
                }

                shape = JsonLiterals.Parse(literal.Value, literal is StringLiteral { IsNational: true }, At(literal));
                if (shape is UnknownShape)
                    return (false, null);
            }
            else
            {
                return (false, null);
            }

            content = content is null ? shape : JsonShapes.Join(content, shape);
        }

        return (true, known ? content : null);
    }

    /// <summary>
    /// Whether a live JSON argument of COALESCE is one the server cannot fold away: a FOR JSON subquery, perhaps in json_query.
    /// COALESCE whose first argument is a constant never NULL is folded to it, keeping its JSON (observed:
    /// Case_iif_choose_and_coalesce_over_json_are_text_unless_the_server_folds_them_to_one_branch); a subquery, even one without
    /// FROM, is not (Coalesce_over_a_for_json_subquery_nests_as_escaped_text_and_isnull_as_json).
    /// </summary>
    private static bool Unfoldable(ScalarExpression expression) => Strip(expression) switch
    {
        ScalarSubquery subquery when ForOf(subquery.QueryExpression) is JsonForClause => true,
        FunctionCall { CallTarget: null, Parameters: [var inner] } call
            when BuiltInFunctions.Named(call.FunctionName.Value, FunctionForm.Scalar) == ScalarFunctions.JsonQuery => Unfoldable(inner),
        _ => false,
    };

    /// <summary>
    /// <c>COALESCE</c> with a JSON argument: text wherever it nests, never live JSON (observed:
    /// Coalesce_over_a_for_json_subquery_nests_as_escaped_text_and_isnull_as_json), nvarchar(max) in a rowset. Its content is
    /// what its arguments hold (<see cref="JsonContent"/>), which json_query makes live again. Null as any COALESCE: a FOR JSON
    /// subquery before the fallback is read twice, so <c>COALESCE((SELECT … FOR JSON PATH), '[]')</c> keeps the subquery's
    /// <c>empty-subquery</c> (observed: Coalesce_reads_a_for_json_subquery_twice_so_it_can_be_null_though_its_fallback_is_not).
    /// </summary>
    private Value CoalesceJson(CoalesceExpression coalesce, List<ScalarExpression> arguments, List<Value> values, EquatableArray<IrNullReason> because, ReadScope scope)
    {
        if (!ReferenceEquals(coalesce, scope.JsonQueryArgument) && arguments.Where((a, i) => values[i].IsLiveJson && !Unfoldable(a)).Any())
            return NotModelled(coalesce, "whether COALESCE over this JSON nests as JSON depends on whether the server folds it.");
        var (json, content) = JsonContent(arguments.Zip(values));
        if (json)
            return new Value(new JsonTextShape(content), because);
        return values.Where((v, i) => !IsJson(v) && TypeOf(v) is not { IsString: true }).Any()
            ? NotModelled(coalesce, "COALESCE of JSON and a value that is not a string is not modelled.")
            : Typed(new ScalarType(BuiltInType.NVarChar, -1), because, coalesce);
    }

    /// <summary>
    /// <c>ISNULL(a, b)</c> with a JSON and maybe null. ISNULL takes a's JSON-ness: over live JSON it is live, and b is written
    /// into the JSON as it is, unchecked (observed: Isnull_writes_its_fallback_into_the_json_as_it_is), so only a b that is JSON
    /// is modelled: a literal that is a JSON document (<c>'[1]'</c> is <c>[1]</c>, observed in
    /// Under_json_query_coalesce_case_and_isnull_with_a_json_literal_give_the_json_of_the_branch_taken; <c>'[1]x'</c> is not
    /// JSON), or a live JSON value or JSON text whose content is known; <c>'null'</c> is JSON null that is not SQL NULL, so a key
    /// holding it is never left out, which is not modelled. Over JSON text (WITHOUT_ARRAY_WRAPPER) it is text. It reads a once, so it is
    /// NULL only when b is too: b's reasons say why.
    /// </summary>
    private Value IsNullJson(FunctionCall call, ScalarExpression a, Value first, ScalarExpression b, ReadScope scope)
    {
        var second = Expression(b, scope);
        if (second.Shape is UnknownShape)
            return second;
        var (json, content) = JsonContent([(a, first), (b, second)]);
        if (first.IsLiveJson)
        {
            return json && content is not null
                ? new Value(content, second.NullableBecause, IsLiveJson: true)
                : NotModelled(call, "ISNULL writes its fallback into the JSON as it is; only '[]', '{}' or JSON is modelled.");
        }

        return json ? new Value(new JsonTextShape(content), second.NullableBecause) : Typed(new ScalarType(BuiltInType.NVarChar, -1), second.NullableBecause, call);
    }
}
