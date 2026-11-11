using System.Globalization;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// Slice 6g: CASE, IIF, CHOOSE and NULLIF, and COALESCE and ISNULL over JSON. README.md in this folder ("CASE, IIF, CHOOSE and
// NULLIF", "COALESCE and ISNULL over JSON") gives the rules and the observations they rest on (CaseObservationTests in the
// differential tests).
internal sealed partial class ProcedureAnalyzer
{
    // ---- what a branch knows --------------------------------------------------------------------------------------------

    /// <summary>
    /// What holds while a branch of a CASE or IIF is read: its WHEN was TRUE, and every earlier WHEN was FALSE or UNKNOWN.
    /// Unlike a WHERE's facts these are about the value itself, read in the same row after any grouping, so a column proved
    /// here is never null whatever else made it nullable (an outer join, a ROLLUP's super-aggregate row): the branch tested
    /// that very value (observed: A_case_branch_runs_only_when_its_condition_is_true_and_later_ones_only_when_earlier_were_not).
    /// </summary>
    /// <param name="query">The query whose row is read, whose aggregates the branch may prove; null outside a query.</param>
    private sealed class BranchFacts(QuerySpecification? query)
    {
        public QuerySpecification? Query { get; } = query;

        /// <summary>Columns proved non-null: by source (by reference, as in QueryFacts), the columns' keys.</summary>
        public Dictionary<BoundSource, HashSet<string>> Values { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>Sources whose row is proved there: a null-extended row has every column NULL.</summary>
        public HashSet<BoundSource> Rows { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>Variables and parameters proved non-null: one has one value throughout an expression.</summary>
        public HashSet<string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Aggregates of <see cref="Query"/> proved non-null, by their text: each is computed once per group.</summary>
        public HashSet<string> Aggregates { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>The branches being read, outermost first.</summary>
    private readonly List<BranchFacts> branches = [];

    private bool BranchProved(BoundSource source, string key) =>
        branches.Any(b => b.Values.TryGetValue(source, out var keys) && keys.Contains(key));

    private bool BranchMatched(BoundSource source) => branches.Any(b => b.Rows.Contains(source));

    private bool BranchProvedAggregate(FunctionCall call) =>
        branches.Any(b => b.Query is not null && ReferenceEquals(b.Query, current) && b.Aggregates.Contains(Text(call)));

    /// <summary>A variable or parameter, never null in a branch that proved it so.</summary>
    private Value Branched(VariableReference variable, Value value) =>
        value.Shape is not UnknownShape && branches.Any(b => b.Variables.Contains(variable.Name)) ? value with { NullableBecause = [] } : value;

    /// <summary>
    /// What a branch knows: <paramref name="held"/> was TRUE (none for an ELSE) and each of <paramref name="failed"/> was FALSE
    /// or UNKNOWN. A value is proved non-null when, were it NULL, no condition could take the truth value it took
    /// (<see cref="Possible"/>, the three-valued logic of refinement). Only columns, variables and the query's aggregates are
    /// proved: each is the same value wherever it is read in the row. A subquery or a call may give another value when read
    /// again (Coalesce_reads_a_subquery_twice…), so testing one proves nothing about it.
    /// </summary>
    private BranchFacts Branch(IReadOnlyList<BooleanExpression> failed, BooleanExpression? held, BindingScope scope)
    {
        var found = new BranchFacts(current);
        if (held is not null)
            Prove(held, Truth.True, scope, found);
        foreach (var condition in failed)
            Prove(condition, Truth.False | Truth.Unknown, scope, found);
        return found;
    }

    private void Prove(BooleanExpression condition, Truth took, BindingScope scope, BranchFacts found)
    {
        bool Impossible(Func<ScalarExpression, bool> target) => (Possible(condition, target) & took) == Truth.None;

        foreach (var operand in Operands(condition))
        {
            switch (operand)
            {
                case ColumnReferenceExpression { ColumnType: ColumnType.Regular } column when Bound(column, scope) is var (source, key):
                    if (Impossible(e => e is ColumnReferenceExpression c && Bound(c, scope) is var (s, k) && ReferenceEquals(s, source) && k == key))
                    {
                        if (!found.Values.TryGetValue(source, out var keys))
                            found.Values[source] = keys = new HashSet<string>(StringComparer.Ordinal);
                        keys.Add(key);
                        found.Rows.Add(source);
                    }

                    break;
                case VariableReference variable:
                    if (Impossible(e => e is VariableReference v && v.Name.Equals(variable.Name, StringComparison.OrdinalIgnoreCase)))
                        found.Variables.Add(variable.Name);
                    break;
                case FunctionCall call when IsAggregate(call) && current is not null:
                    var text = Text(call);
                    if (Impossible(e => e is FunctionCall c && IsAggregate(c) && Text(c) == text))
                        found.Aggregates.Add(text);
                    break;
            }
        }
    }

    private (BoundSource Source, string Key)? Bound(ColumnReferenceExpression column, BindingScope scope) => binder.BindColumn(column, scope) switch
    {
        BoundColumn c => (c.Source, Key(c)),
        BoundLocalColumn { Ordinal: >= 0 } l => (l.Source, Key(l)),
        _ => null,
    };

    private T InBranch<T>(BranchFacts found, Func<T> read)
    {
        branches.Add(found);
        try
        {
            return read();
        }
        finally
        {
            branches.RemoveAt(branches.Count - 1);
        }
    }

    // ---- CASE and IIF ---------------------------------------------------------------------------------------------------

    /// <summary>A searched CASE: each WHEN's condition, its result, and the ELSE.</summary>
    private Value Case(SearchedCaseExpression expression, BindingScope scope) =>
        Branches(expression, [.. expression.WhenClauses.Select(w => (w.WhenExpression, w.ThenExpression))], expression.ElseExpression, scope);

    /// <summary>
    /// A simple CASE, <c>CASE x WHEN 1 THEN …</c>, compares with <c>=</c> (observed: WHEN NULL never matches), so each WHEN is
    /// read as <c>x = 1</c>.
    /// </summary>
    private Value Case(SimpleCaseExpression expression, BindingScope scope) =>
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
    private Value Iif(IIfCall call, BindingScope scope) =>
        Branches(call, [(call.Predicate, call.ThenExpression)], call.ElseExpression, scope);

    /// <summary>
    /// The branches of a CASE: each read knowing what held (<see cref="Branch"/>). Its type is the precedence over its results
    /// (<see cref="Results(TSqlFragment, List{ValueTuple{ScalarExpression, Value}}, IReadOnlyList{IrNullReason})"/>); without ELSE it is NULL when no WHEN holds (<c>no-else</c>).
    /// </summary>
    private Value Branches(TSqlFragment at, IReadOnlyList<(BooleanExpression When, ScalarExpression Then)> whens, ScalarExpression? otherwise, BindingScope scope)
    {
        var results = new List<(ScalarExpression Expression, Value? Value)>();
        var failed = new List<BooleanExpression>();
        foreach (var (when, then) in whens)
        {
            results.Add((then, Strip(then) is NullLiteral ? null : InBranch(Branch(failed, when, scope), () => Expression(then, scope))));
            failed.Add(when);
        }

        if (otherwise is not null)
            results.Add((otherwise, Strip(otherwise) is NullLiteral ? null : InBranch(Branch(failed, null, scope), () => Expression(otherwise, scope))));
        return Results(at, results, otherwise is null ? [new IrNullReason(IrNullRules.NoElse, At(at).ToString())] : []);
    }

    /// <summary>
    /// The value of a CASE, IIF or CHOOSE from its results (a null value for the NULL literal): the type of highest precedence
    /// over them, as COALESCE's (<see cref="ScalarType.Common"/>; observed:
    /// Case_iif_and_choose_take_the_type_of_highest_precedence_over_their_results); null when a result may be, or is the NULL
    /// literal (<c>null-literal</c>), or converting one to the type may overflow (<c>arithmetic-overflow</c>), or for
    /// <paramref name="also"/>. When
    /// every other result is a string literal, or every one a number, it is one of them exactly: a literal is varchar or
    /// nvarchar, never padded, so trailing spaces are its own (observed:
    /// A_literal_result_keeps_its_trailing_spaces_and_a_fixed_length_result_pads). Live JSON is not modelled: whether a CASE
    /// over it nests as JSON depends on whether the server folds it to one branch (observed:
    /// Case_iif_choose_and_coalesce_over_json_are_text_unless_the_server_folds_them_to_one_branch). Results that are all JSON
    /// text are JSON text of their join, and under json_query, where folding does not matter, so are any (slice 6l).
    /// </summary>
    private Value Results(TSqlFragment at, List<(ScalarExpression Expression, Value? Value)> results, IReadOnlyList<IrNullReason> also)
    {
        var given = results.Where(r => r.Value is not null).Select(r => (r.Expression, Value: r.Value!)).ToList();
        if (given.Count == 0)
            return NotModelled(at, "every result is NULL.");
        if (given.FirstOrDefault(r => r.Value.Shape is UnknownShape) is { Value: { } unknown })
            return unknown;

        // Under json_query whether the server folds it no longer matters: json_query makes either result JSON, so what it holds
        // is the join of its results' JSON, literals parsed (slice 6l). Elsewhere, JSON text in every result is JSON text.
        var underJsonQuery = ReferenceEquals(at, jsonQueryArgument);
        if (given.Any(r => r.Value.IsLiveJson) && !underJsonQuery)
            return NotModelled(at, "whether it nests as JSON depends on whether the server folds it to one result.");
        if (underJsonQuery || given.Any(r => r.Value.Shape is JsonTextShape))
        {
            var (json, content) = JsonContent(given.Select(r => (r.Expression, r.Value)));
            if (json)
            {
                var nulls = given.Select(r => r.Value.NullableBecause)
                    .Concat(results.Where(r => r.Value is null).Select(r => (IReadOnlyList<IrNullReason>)[new IrNullReason(IrNullRules.NullLiteral, At(r.Expression).ToString())]))
                    .Append(also)
                    .Aggregate((IReadOnlyList<IrNullReason>)[], ShapeJoin.Reasons);
                return new Value(new JsonTextShape(content), nulls);
            }
        }

        var where = At(at);
        var combined = Combined([.. given.Select(r => (r.Value, r.Expression, where))]);
        if (combined.Type is not { } type)
            return NotModelled(at, combined.Why!);

        var because = given.Select(r => r.Value.NullableBecause)
            .Concat(combined.Overflows)
            .Concat(results.Where(r => r.Value is null).Select(r => (IReadOnlyList<IrNullReason>)[new IrNullReason(IrNullRules.NullLiteral, At(r.Expression).ToString())]))
            .Append(also)
            .Aggregate((IReadOnlyList<IrNullReason>)[], ShapeJoin.Reasons);
        // A CASE whose condition is constant is folded to one result, an int literal then counting its own digits (observed:
        // An_int_constant_counts_its_own_digits_beside_a_decimal); which, is not known here.
        var value = Typed(type, because, at) with { Digits = type.Name == "int" ? IntDigits.Foldable(given.Select(r => r.Value.Digits)) : null };
        return LiteralSet(given.Select(r => r.Expression), type, at) is { } set && value.Shape is ScalarShape scalar
            ? value with { Shape = scalar with { Type = scalar.Type with { Facets = (scalar.Type.Facets ?? new IrFacets()) with { Values = set } } } }
            : value;
    }

    /// <summary>
    /// The exact set of literal results, when every result is one: strings for a string type, numbers for an exact number. A
    /// literal converts to the type without changing (the type is wide enough for each, and an ASCII varchar literal is the
    /// same in nvarchar). Items are in a stable order, each once.
    /// </summary>
    private IrValueSet? LiteralSet(IEnumerable<ScalarExpression> results, ScalarType type, TSqlFragment at)
    {
        var literals = results.Select(LiteralOf).ToList();
        if (literals.Any(l => l is null))
            return null;
        IReadOnlyList<IrValue> items;
        if (type.IsString && literals.All(l => l!.Value.Literal is StringLiteral))
        {
            // A string longer than the type is cut short (a national type stops at 4000 characters).
            if (!type.IsMax && literals.Any(l => l!.Value.Literal.Value.Length > type.Length))
                return null;
            items = [.. literals.Select(l => l!.Value.Literal.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(s => new IrValue(s, IsString: true))];
        }
        else if ((type.IsInteger || type.IsDecimal) && literals.All(l => l!.Value.Literal is IntegerLiteral or NumericLiteral))
        {
            var numbers = new List<ExactDecimal>();
            foreach (var (literal, negative) in literals.Select(l => l!.Value))
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

        return new IrValueSet(items, IrValueMatch.Exact, [new IrFactSource(IrFactSources.Expression, At(at).ToString())]);
    }

    // ---- CHOOSE ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>CHOOSE(i, a, b, …)</c>: the precedence over its values, as CASE (<see cref="Results(TSqlFragment, List{ValueTuple{ScalarExpression, Value}}, IReadOnlyList{IrNullReason})"/>). NULL when i is, or is below 1
    /// or above the count (<c>out-of-range</c>; observed: Case_without_else_choose_out_of_range_and_nullif_of_equal_values_are_null),
    /// unless i is an integer literal in range or an integer whose bounds (a CHECK's) keep it in range. Its values are not read
    /// as branches: which one is taken is a number, not a condition.
    /// </summary>
    private Value Choose(FunctionCall call, BindingScope scope)
    {
        var count = call.Parameters.Count - 1;
        var index = Expression(call.Parameters[0], scope);
        var results = call.Parameters.Skip(1).Select(p => (p, Strip(p) is NullLiteral ? null : Expression(p, scope))).ToList();
        var because = InRange(index, call.Parameters[0], count)
            ? index.NullableBecause
            : ShapeJoin.Reasons(index.NullableBecause, [new IrNullReason(IrNullRules.OutOfRange, At(call).ToString())]);
        return Results(call, results, because);
    }

    private static bool InRange(Value index, ScalarExpression expression, int count)
    {
        if (LiteralOf(expression) is ({ } literal and IntegerLiteral, var negative))
            return !negative && long.TryParse(literal.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= count;
        if (index.Shape is not ScalarShape { Type: { Sql: var sql, Facets: { Min: { } min, Max: { } max } } } || ScalarType.Parse(sql) is not { IsInteger: true })
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
    /// <c>NULLIF(a, b)</c>: a's value, unchanged, or NULL when a and b compare equal (<c>nullif</c>), so a's type and facts; never
    /// equal when b is the NULL literal. An integer literal a is the smallest integer type that holds it (observed:
    /// Nullif_is_its_first_arguments_type_but_an_integer_literal_is_the_smallest_integer_holding_it). Over JSON it is text
    /// unless the server folds it (observed, as for CASE), which is not modelled.
    /// </summary>
    private Value NullIf(NullIfExpression nullIf, BindingScope scope)
    {
        if (Strip(nullIf.FirstExpression) is NullLiteral)
            return NotModelled(nullIf, "NULLIF of the NULL literal is an error.");
        var value = Expression(nullIf.FirstExpression, scope);
        if (Strip(nullIf.SecondExpression) is NullLiteral || value.Shape is UnknownShape)
            return value;
        if (value.IsLiveJson || value.Shape is JsonTextShape)
            return NotModelled(nullIf, "whether NULLIF over JSON nests as JSON depends on whether the server folds it.");

        var because = ShapeJoin.Reasons(value.NullableBecause, [new IrNullReason(IrNullRules.NullIf, At(nullIf).ToString())]);
        if (LiteralOf(nullIf.FirstExpression) is ({ } literal and IntegerLiteral, var negative)
            && long.TryParse(literal.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
        {
            n = negative ? -n : n;
            var smallest = n is >= byte.MinValue and <= byte.MaxValue ? "tinyint" : n is >= short.MinValue and <= short.MaxValue ? "smallint" : "int";
            return Typed(new ScalarType(smallest), because, nullIf);
        }

        return value with { NullableBecause = because, Digits = IntDigits.Foldable([value.Digits]) };
    }

    // ---- COALESCE and ISNULL over JSON ----------------------------------------------------------------------------------

    private static bool IsJson(Value value) => value.IsLiveJson || value.Shape is JsonTextShape;

    /// <summary>
    /// What a value built from <paramref name="parts"/> holds as JSON, each written as it is: a live value its shape, JSON text its
    /// content, and a literal that is a JSON document its parsed shape (<see cref="JsonLiterals"/>; slice 6l, before which only
    /// <c>'[]'</c> and <c>'{}'</c> were read). <c>NotJson</c> when a part may not be JSON (a column, a literal that is not a
    /// document); otherwise JSON, its content null when not known (a JSON text of unknown content, or <c>'null'</c>, which is
    /// JSON but not a document). The content is the join of the parts' (<see cref="ShapeJoin.Content"/>): <c>'[]'</c> lets
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

            content = content is null ? shape : ShapeJoin.Content(content, shape);
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
        FunctionCall { CallTarget: null, Parameters: [var inner] } call when call.FunctionName.Value.Equals("json_query", StringComparison.OrdinalIgnoreCase) => Unfoldable(inner),
        _ => false,
    };

    /// <summary>
    /// <c>COALESCE</c> with a JSON argument: text wherever it nests, never live JSON (observed:
    /// Coalesce_over_a_for_json_subquery_nests_as_escaped_text_and_isnull_as_json), nvarchar(max) in a rowset. Its content is
    /// what its arguments hold (<see cref="JsonContent"/>), which json_query makes live again. Null as any COALESCE: a FOR JSON
    /// subquery before the fallback is read twice, so <c>COALESCE((SELECT … FOR JSON PATH), '[]')</c> keeps the subquery's
    /// <c>empty-subquery</c> (observed: Coalesce_reads_a_for_json_subquery_twice_so_it_can_be_null_though_its_fallback_is_not).
    /// </summary>
    private Value CoalesceJson(CoalesceExpression coalesce, List<ScalarExpression> arguments, List<Value> values, IReadOnlyList<IrNullReason> because)
    {
        if (!ReferenceEquals(coalesce, jsonQueryArgument) && arguments.Where((a, i) => values[i].IsLiveJson && !Unfoldable(a)).Any())
            return NotModelled(coalesce, "whether COALESCE over this JSON nests as JSON depends on whether the server folds it.");
        var (json, content) = JsonContent(arguments.Zip(values));
        if (json)
            return new Value(new JsonTextShape(content), because);
        return values.Where((v, i) => !IsJson(v) && TypeOf(v, arguments[i]) is not { IsString: true }).Any()
            ? NotModelled(coalesce, "COALESCE of JSON and a value that is not a string is not modelled.")
            : Typed(new ScalarType("nvarchar", -1), because, coalesce);
    }

    /// <summary>
    /// <c>ISNULL(a, b)</c> with a JSON and maybe null. ISNULL takes a's JSON-ness: over live JSON it is live, and b is written
    /// into the JSON as it is, unchecked (observed: Isnull_writes_its_fallback_into_the_json_as_it_is), so only a b that is JSON
    /// is modelled: a literal that is a JSON document (slice 6l: <c>'[1]'</c> is <c>[1]</c>, observed in
    /// Under_json_query_coalesce_case_and_isnull_with_a_json_literal_give_the_json_of_the_branch_taken; <c>'[1]x'</c> is not
    /// JSON), or a live JSON value or JSON text whose content is known; <c>'null'</c> is JSON null that is not SQL NULL, so a key
    /// holding it is never left out, which is not modelled. Over JSON text (WITHOUT_ARRAY_WRAPPER) it is text. It reads a once, so it is
    /// NULL only when b is too: b's reasons say why.
    /// </summary>
    private Value IsNullJson(FunctionCall call, ScalarExpression a, Value first, ScalarExpression b, BindingScope scope)
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

        return json ? new Value(new JsonTextShape(content), second.NullableBecause) : Typed(new ScalarType("nvarchar", -1), second.NullableBecause, call);
    }
}
