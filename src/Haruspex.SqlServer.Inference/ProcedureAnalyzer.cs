using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Infers the result sets of one procedure. Every construct it does not model becomes an
/// <see cref="UnknownShape"/> with a reason, so what it does claim can be trusted.
/// </summary>
/// <remarks>
/// Which table and column a reference reads is the <see cref="Binder"/>'s job (Haruspex.SqlServer.Semantics);
/// what that means for a value's shape and nullability is decided here. They are apart so
/// that lint, lineage and diff can bind without inferring.
/// </remarks>
internal sealed partial class ProcedureAnalyzer
{
    private readonly DatabaseModel model;
    private readonly Binder binder;
    private readonly string file;
    private readonly FunctionSummaries functions;

    /// <summary>Whether the whole body is read uncommitted where it is used (see <see cref="ReadUncommitted"/>).</summary>
    private bool BodyReadUncommitted { get; }

    /// <summary>What each view holds (ProcedureAnalyzer.Views.cs); null leaves views unmodelled.</summary>
    private readonly ViewSummaries? views;

    /// <summary>Inside a function's body, its parameters by name; empty in a procedure, whose parameters are not followed yet.</summary>
    private readonly IReadOnlyDictionary<string, Value> parameters;

    /// <summary>The query whose select list is being read, for an aggregate to know whether it has GROUP BY.</summary>
    private QuerySpecification? current;

    /// <param name="model">The model the body's references resolve in.</param>
    /// <param name="file">The file the body is in, for locations.</param>
    /// <param name="functions">What each scalar function returns.</param>
    /// <param name="parameters">Inside a function, its parameters' values.</param>
    /// <param name="calls">The calls being followed, inside a procedure; null inside a function, which cannot call one.</param>
    /// <param name="context">What a procedure's walk starts from: its caller's temporary tables, and how it is called.</param>
    /// <param name="ansiNullsOff">Whether the body was created with ANSI_NULLS off (see <see cref="ansiNullsOff"/>).</param>
    /// <param name="views">What each view of the project holds, read from its definition; null leaves views unmodelled.</param>
    /// <param name="uncommittedReads">
    /// Whether the body (a view's, a function's) is read uncommitted where it is used, so that no key of what it reads holds.
    /// </param>
    public ProcedureAnalyzer(
        DatabaseModel model,
        string file,
        FunctionSummaries functions,
        IReadOnlyDictionary<string, Value>? parameters = null,
        ProcedureCalls? calls = null,
        CallContext? context = null,
        bool ansiNullsOff = false,
        ViewSummaries? views = null,
        bool uncommittedReads = false)
    {
        BodyReadUncommitted = uncommittedReads;
        this.views = views;
        this.ansiNullsOff = ansiNullsOff;
        this.model = model;
        binder = new Binder(model, file);
        this.file = file;
        this.functions = functions;
        this.parameters = parameters ?? new Dictionary<string, Value>();
        this.calls = calls;
        this.context = context ?? CallContext.Top;
        escapes = this.context.Escapes ? [] : null;
    }

    /// <summary>
    /// The value a function's <c>RETURN</c> expression gives, read with no FROM in scope; its subqueries may read views and
    /// table-valued functions.
    /// </summary>
    /// <param name="expression">The returned expression.</param>
    /// <param name="parameters">
    /// The function's parameters, which are the same for every row its queries read, so a key compared with one is fixed, as
    /// in an inline function.
    /// </param>
    public Value Return(ScalarExpression expression, IEnumerable<ProcedureParameter> parameters)
    {
        foreach (var parameter in parameters)
            variableTypes[parameter.VariableName.Value] = SqlType.From(parameter.DataType);
        locals = Locals(new Dictionary<string, LocalRelationValues>(StringComparer.OrdinalIgnoreCase));
        return Expression(expression, binder.BindFrom(new QuerySpecification(), null, locals));
    }

    /// <summary>A procedure's declared parameters and their types.</summary>
    public static IReadOnlyList<ParameterShape> Parameters(DatabaseModel model, Procedure procedure) =>
    [
        .. procedure.Body.Parameters.Select(p => new ParameterShape(
            p.VariableName.Value,
            SqlTypes.ToShape(model, SqlType.From(p.DataType), new SourceLocation(procedure.File, p.StartLine, p.StartColumn)),
            HasDefault: p.Value is not null,
            IsOutput: p.Modifier == ParameterModifier.Output)),
    ];

    /// <summary>
    /// Session options known not to change which result sets run or their shape. Anything else is not modelled:
    /// NOEXEC, FMTONLY and PARSEONLY stop the rows from being returned at all, and turning off ANSI_WARNINGS
    /// or CONCAT_NULL_YIELDS_NULL changes where expressions produce null.
    /// </summary>
    private const SetOptions AnyValue = SetOptions.NoCount | SetOptions.XactAbort;

    private const SetOptions OnlyOn =
        SetOptions.AnsiNulls | SetOptions.AnsiPadding | SetOptions.AnsiWarnings | SetOptions.QuotedIdentifier | SetOptions.ArithAbort;

    private static bool IsHarmless(PredicateSetStatement set) =>
        (set.Options & ~(set.IsOn ? AnyValue | OnlyOn : AnyValue)) == SetOptions.None;

    /// <summary>The result set a SELECT returns, or null if it returns none.</summary>
    private ResultShape? Select(SelectStatement select)
    {
        // SELECT ... INTO writes a table and SELECT @v = ... assigns variables; neither returns a result set.
        if (select.Into is not null)
            return null;
        if (select.QueryExpression is QuerySpecification { SelectElements: var elements } && elements.All(e => e is SelectSetVariable))
            return null;

        var at = At(select);
        var encoding = ForOf(select.QueryExpression) is JsonForClause ? IrEncoding.Json : IrEncoding.Rowset;

        // A top-level FOR JSON query that matches no rows returns no row at all, rather than [] or {}. A
        // rowset that matches no rows is still a result set, with no rows. A query that always returns exactly one row
        // always returns its JSON (observed: For_json_over_an_aggregate_without_group_by_always_returns_its_one_object).
        EquatableArray<IrNullReason> NoRows(Row row) => encoding == IrEncoding.Json && row.Rows != IrCardinality.One ? [new IrNullReason(IrNullRule.NoRows, at.ToString())] : [];
        EquatableArray<IrNullReason> absent = encoding == IrEncoding.Json ? [new IrNullReason(IrNullRule.NoRows, at.ToString())] : [];

        return WithCtes(
            select.WithCtesAndXmlNamespaces,
            () =>
            {
                // A set operation's FOR, ORDER BY and OFFSET belong to the whole (observed:
                // Order_by_and_offset_apply_to_the_whole_set_operation_and_top_to_its_first_query).
                var query = select.QueryExpression;
                var row = Query(query, parent: null);
                return ForOf(query) switch
                {
                    JsonForClause json => new ResultShape(IrEncoding.Json, ForJson(row, json, subquery: false), NoRows(row), row.Rows),
                    null => new ResultShape(IrEncoding.Rowset, Rowset(row), [], row.Rows),
                    var other => new ResultShape(IrEncoding.Rowset, Unsupported(IrUnknownReason.UnsupportedQuery, $"{Text(other)} is not modelled yet.", at), []),
                };
            },
            why => new ResultShape(encoding, why, absent));
    }

    // ---- queries ------------------------------------------------------------------------------------

    /// <param name="Rows">How many rows the query returns.</param>
    /// <param name="Sources">The sources of its FROM, for a view's keys.</param>
    private sealed record Row(IReadOnlyList<RowColumn> Columns, SourceLocation? Star, IrCardinality Rows = IrCardinality.Many, IReadOnlyList<BoundSource>? Sources = null);

    private Row Query(QuerySpecification query, BindingScope? parent)
    {
        // An outermost query sees the body's own relations; a subquery inherits them through its parent's scope.
        var scope = binder.BindFrom(query, parent, parent is null ? locals : null);

        // WHERE, the joins' ON and HAVING prove columns and aggregates non-null in the rows they keep (Refinement.cs), for
        // the select list and every subquery in it; GROUP BY is not read beyond what cardinality needs.
        var columns = new List<RowColumn>();
        SourceLocation? star = null;
        var outer = current;
        current = query;
        var found = Filters(query, scope);

        // A cursor's row is read again when it is fetched, so its query's filters prove nothing of it (FetchRow).
        if (unfiltered)
            found.Forget();

        var outside = facts;
        facts = facts.With(found);
        try
        {
            foreach (var element in query.SelectElements)
            {
                switch (element)
                {
                    case SelectScalarExpression scalar:
                        // $action is named so, as the server names it (observed: Merge_action_is_an_nvarchar_10).
                        var name = scalar.ColumnName?.Value
                            ?? (scalar.Expression is ColumnReferenceExpression { ColumnType: ColumnType.PseudoColumnAction } ? "$action" : null)
                            ?? (scalar.Expression as ColumnReferenceExpression)?.MultiPartIdentifier?.Identifiers[^1].Value;
                        columns.Add(new RowColumn(name, Expression(scalar.Expression, scope), At(scalar), Origin(scalar.Expression, scope), scalar.Expression));
                        break;
                    case SelectStarExpression all when expandStars && Expand(all, scope) is { } expanded:
                        columns.AddRange(expanded);
                        break;
                    case SelectStarExpression:
                        star ??= At(element);
                        break;
                }
            }
        }
        finally
        {
            facts = outside;
            current = outer;
        }

        return new Row(columns, star, Cardinality(query, scope), scope.Sources);
    }

    /// <summary>The table column a select-list expression is, unchanged: a bare column reference without COLLATE.</summary>
    private BoundColumn? Origin(ScalarExpression expression, BindingScope scope) =>
        expression is ColumnReferenceExpression { Collation: null, ColumnType: ColumnType.Regular } column && binder.BindColumn(column, scope) is BoundColumn bound
            ? bound
            : null;

    private static Shape Rowset(Row row)
    {
        if (row.Star is { } star)
            return Unsupported(IrUnknownReason.UnsupportedQuery, "SELECT * is not expanded yet.", star);
        if (row.Columns.FirstOrDefault(c => string.IsNullOrEmpty(c.Name)) is { } unnamed)
            return Unsupported(IrUnknownReason.UnnamedColumn, $"The column {Text(unnamed.At)} has no name.", unnamed.At);

        // Drivers key a row by column name, and the mssql driver turns a repeated name into an array, so a
        // repeated name changes the row's shape.
        var duplicate = row.Columns.GroupBy(c => c.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            return Unsupported(IrUnknownReason.DuplicateColumn, $"The column name '{duplicate.Key}' is used more than once.", duplicate.Skip(1).First().At);

        var fields = row.Columns.Select(c => new FieldShape(
            c.Name!,
            // A rowset column delivers JSON as text: the driver sees an nvarchar. Its content is what it parses to.
            c.Value.IsLiveJson ? new JsonTextShape(c.Value.Shape) : c.Value.Shape,
            IrPresence.Always,
            c.Value.NullableBecause));
        return new ArrayShape(new ObjectShape([.. fields]), []);
    }

    // ---- FOR JSON -----------------------------------------------------------------------------------

    private sealed class JsonNode
    {
        public List<(string Key, JsonNode? Child, RowColumn? Leaf, SourceLocation At)> Entries { get; } = [];
    }

    /// <summary>
    /// The shape <c>FOR JSON PATH</c> builds from a row: one object per row, with a dotted alias such as
    /// <c>customer.name</c> nesting a key inside an object.
    /// </summary>
    /// <param name="subquery">
    /// Whether it is a subquery's value, which is NULL over no rows (observed), so that an array it builds has at least one
    /// item, and at most one when the query returns at most one row. A result's bounds are its cardinality instead, since
    /// a result that may be absent reads as [] (the IR's reader convention).
    /// </param>
    private Shape ForJson(Row row, JsonForClause clause, bool subquery)
    {
        var options = clause.Options.Select(o => o.OptionKind).ToHashSet();
        if (options.Contains(JsonForClauseOptions.Auto))
            return Unsupported(IrUnknownReason.UnsupportedQuery, "FOR JSON AUTO is not modelled yet.", At(clause));
        if (options.Contains(JsonForClauseOptions.Root))
            return Unsupported(IrUnknownReason.UnsupportedQuery, "FOR JSON ... ROOT is not modelled yet.", At(clause));
        if (row.Star is { } star)
            return Unsupported(IrUnknownReason.UnsupportedQuery, "SELECT * is not expanded yet.", star);

        var root = new JsonNode();
        foreach (var column in row.Columns)
        {
            // SQL Server rejects an unnamed column in FOR JSON, so the procedure fails rather than returning
            // anything; it is reported rather than modelled.
            if (string.IsNullOrEmpty(column.Name))
                return Unsupported(IrUnknownReason.UnnamedColumn, $"The column {Text(column.At)} has no name, which FOR JSON rejects.", column.At);

            var path = column.Name.Split('.');
            if (path.Any(p => p.Length == 0))
                return Unsupported(IrUnknownReason.UnsupportedQuery, $"The alias '{column.Name}' is not a valid FOR JSON path.", column.At);

            var node = root;
            foreach (var key in path[..^1])
            {
                // Keys of one nested object must come from adjacent columns. Anything else (a key reused after
                // another column, a key that is both a value and an object, or the same key spelt in another
                // case) either fails in SQL Server or writes duplicate keys, so it is not modelled.
                if (node.Entries.Count > 0 && node.Entries[^1] is { Child: { } child } last && last.Key.Equals(key, StringComparison.Ordinal))
                {
                    node = child;
                    continue;
                }

                if (node.Entries.Any(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
                    return KeyConflict(column);
                var nested = new JsonNode();
                node.Entries.Add((key, nested, null, column.At));
                node = nested;
            }

            if (node.Entries.Any(e => e.Key.Equals(path[^1], StringComparison.OrdinalIgnoreCase)))
                return KeyConflict(column);
            node.Entries.Add((path[^1], null, column, column.At));
        }

        var includeNulls = options.Contains(JsonForClauseOptions.IncludeNullValues);
        var shape = JsonObject(root, includeNulls);

        // WITHOUT_ARRAY_WRAPPER is claimed as one object even where the query is not proven to return at most one row
        // (Row.Rows is Many). The claim is accepted because it cannot be silently wrong: several rows produce
        // "{...},{...}", which is not valid JSON, so the caller's parse fails loudly before any schema sees it. Where the
        // query is proven to return at most one row, the result's cardinality says so and the caveat does not apply. No
        // rows are covered by AbsentBecause.
        if (options.Contains(JsonForClauseOptions.WithoutArrayWrapper))
            return shape;
        return subquery ? new ArrayShape(shape, [], MinItems: 1, MaxItems: row.Rows == IrCardinality.Many ? null : 1) : new ArrayShape(shape, []);
    }

    private static UnknownShape KeyConflict(RowColumn column) =>
        new(IrUnknownReason.DuplicateColumn, $"The alias '{column.Name}' clashes with another key in the same JSON object.", column.At);

    private static ObjectShape JsonObject(JsonNode node, bool includeNulls)
    {
        var fields = new List<FieldShape>();
        foreach (var (key, child, leaf, at) in node.Entries)
        {
            if (leaf is not null)
            {
                var value = leaf.Value;

                // FOR JSON leaves out a key whose value is null, unless INCLUDE_NULL_VALUES keeps it as null.
                // Live JSON keeps its shape; JSON text (JsonTextShape) is escaped into a string.
                var presence = value.NullableBecause.Count > 0 && !includeNulls ? IrPresence.OmittedWhenNull : IrPresence.Always;
                fields.Add(new FieldShape(key, value.Shape, presence, value.NullableBecause));
                continue;
            }

            var nested = JsonObject(child!, includeNulls);

            // A nested object whose every value is null is left out of the output. With INCLUDE_NULL_VALUES it is
            // always written, with each key null, however deep it is nested: {"a":{"b":{"y":null}}}. Both were
            // observed on SQL Server 2025 and are kept as tests (ForJsonObservationTests, differential tests).
            var allNull = !includeNulls && nested.Fields.All(f => f.NullableBecause.Count > 0);
            fields.Add(allNull
                ? new FieldShape(key, nested, IrPresence.OmittedWhenNull, [new IrNullReason(IrNullRule.AllFieldsNull, at.ToString())])
                : new FieldShape(key, nested, IrPresence.Always, []));
        }

        return new ObjectShape([.. fields]);
    }

    // ---- expressions --------------------------------------------------------------------------------

    private Value Expression(ScalarExpression expression, BindingScope scope) => expression switch
    {
        ColumnReferenceExpression { ColumnType: ColumnType.PseudoColumnAction } action => MergeAction(action),
        ColumnReferenceExpression column => Column(column, scope),
        ParenthesisExpression parenthesis => Expression(parenthesis.Expression, scope),
        ScalarSubquery subquery => Subquery(subquery, scope),

        // Every built-in function, by the function table (ProcedureAnalyzer.FunctionTable.cs): the one path a call is typed by.
        _ when Function(expression, scope) is { } called => called,
        FunctionCall call when FunctionSummaries.Callee(call) is { } callee => CallFunction(call, callee, scope),
        VariableReference variable when parameters.TryGetValue(variable.Name, out var parameter) => Branched(variable, parameter),
        VariableReference variable when locals is not null => Branched(variable, Variable(variable)),
        IntegerLiteral literal => Integer(literal),
        StringLiteral literal => FromString(literal),
        NumericLiteral literal => FromNumeric(literal),
        RealLiteral literal => Typed(ScalarType.Of(BuiltInType.Float), [], literal),
        MoneyLiteral literal => Typed(ScalarType.Of(BuiltInType.Money), [], literal),
        NullLiteral literal => Typed(ScalarType.Of(BuiltInType.Int), [new IrNullReason(IrNullRule.NullLiteral, At(literal).ToString())], literal),
        SearchedCaseExpression searched => Case(searched, scope),
        SimpleCaseExpression simple => Case(simple, scope),
        BinaryExpression binary => Binary(binary, scope),
        UnaryExpression unary => Unary(unary, scope),
        _ => Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"The expression {Text(expression)} is not modelled yet.", At(expression))),
    };

    private Value Column(ColumnReferenceExpression column, BindingScope scope) => binder.BindColumn(column, scope) switch
    {
        var target when ReadsOutputTarget(target) => Value.Unknown(Unsupported(
            IrUnknownReason.UnsupportedExpression,
            $"{Text(column)} names the table the write changes, which OUTPUT may read before or after the change; read inserted or deleted instead.",
            At(column))),
        BoundColumn bound => FromColumn(bound),
        BoundLocalColumn local => FromLocal(local, At(column)),
        UnboundColumn unbound => Value.Unknown(Unknown(unbound.Problem)),
        _ => throw new InvalidOperationException("Unknown binding."),
    };

    /// <remarks>
    /// The column's facts (length, digits, bounds, value sets) travel with the value only while it passes through
    /// unchanged: a column reference, through aliases, joins and FOR JSON nesting. Any expression makes the value
    /// something else, and none is modelled yet, so it becomes unknown and the facts go with it.
    /// </remarks>
    private Value FromColumn(BoundColumn bound)
    {
        var (source, table, column) = bound;
        var because = new List<IrNullReason>();
        if (!SchemaRules.IsNeverNull(model, table, column) && facts.Proved(source, Key(bound)) == Facts.Proof.None)
            because.Add(new IrNullReason(IrNullRule.NullableColumn, $"{table.Name}.{column.Name}"));

        // The optional side of an outer join, or of an OUTER APPLY, has no row when nothing matches, so every column read
        // from it can be null, whatever its declaration says; unless a filter proves the row matched.
        if (source.OptionalSideOf is { } optional && !facts.Matched(source))
            because.Add(new IrNullReason(source.OuterApply ? IrNullRule.UnmatchedLateralRow : IrNullRule.UnmatchedRow, optional.ToString()));
        if (Rolled(source) is { } rolled)
            because.Add(rolled);
        if (Unmatched(source) is { } unmatched)
            because.Add(unmatched);

        // A CASE branch that tested this very value proves it, whatever made it nullable (ProcedureAnalyzer.Case.cs).
        if (facts.Proved(source, Key(bound)) == Facts.Proof.OfTheValue)
            because.Clear();

        // A json column nests as JSON inside FOR JSON and the constructors (observed), but what it holds is data.
        // Through a driver it is text, which Rowset makes of any live value.
        if (!column.IsComputed && SchemaRules.ResolveType(model, column.Type)?.Type == BuiltInType.Json)
        {
            return new Value(
                Unsupported(IrUnknownReason.JsonShapeUnknown, $"{table.Name}.{column.Name} is a json column: its shape is in the data, not the schema.", column.At),
                [.. because],
                IsLiveJson: true);
        }

        return Value.Computed(ColumnShape(table, column), [.. because]);
    }

    /// <summary>A column's shape when it is not null.</summary>
    private Shape ColumnShape(Table table, Column column) => column.IsComputed
        ? new UnknownShape(IrUnknownReason.UnsupportedSqlType, $"{table.Name}.{column.Name} is a computed column, which is not modelled yet.", column.At)
        : SqlTypes.ToShape(SchemaRules.ColumnFacts(model, table, column), column.Type, column.At);

    /// <summary>
    /// The IR's reason for a binding problem. A table changed after its definition was reported as an unsupported
    /// source before binding had a layer of its own, and still is; a reference form the binder does not model is
    /// an unsupported expression.
    /// </summary>
    private static UnknownShape Unknown(BindingProblem problem) => new(
        problem.Kind switch
        {
            BindingProblemKind.UnsupportedSource or BindingProblemKind.TableRedefined => IrUnknownReason.UnsupportedSource,
            BindingProblemKind.UnresolvedTable => IrUnknownReason.UnresolvedTable,
            BindingProblemKind.UnresolvedColumn => IrUnknownReason.UnresolvedColumn,
            BindingProblemKind.UnsupportedReference => IrUnknownReason.UnsupportedExpression,
        },
        problem.Detail,
        problem.At);

    private Value Subquery(ScalarSubquery subquery, BindingScope scope)
    {
        var at = At(subquery);

        // A scalar subquery's value is its one column's, with its type and facts, and null when it returns no row; more
        // than one row is an error, not a value. A query that returns exactly one row (an aggregate without GROUP BY, or no
        // FROM) is never empty (observed: A_scalar_subquery_of_an_aggregate_without_group_by_always_has_a_value_for_count).
        // JSON stays live through it: observed for a constructor, an aggregate, json_query, a json value and a FOR JSON
        // subquery (with its wrapper) inside it.
        if (ForOf(subquery.QueryExpression) is null)
        {
            var plain = subquery.QueryExpression;
            var row = Query(plain, scope);
            if (row is not { Star: null, Columns: [var only] })
                return Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"A subquery of other than one listed column is not modelled: {Text(subquery)}", at));
            if (only.Value.Shape is UnknownShape)
                return only.Value;
            // A scalar subquery is not folded: (SELECT 1) counts an int's 10 digits beside a decimal (observed:
            // An_int_constant_counts_its_own_digits_beside_a_decimal).
            return row.Rows == IrCardinality.One
                ? only.Value with { Digits = IntDigits.OfType }
                : only.Value with { Digits = IntDigits.OfType, NullableBecause = [.. only.Value.NullableBecause, new IrNullReason(IrNullRule.EmptySubquery, at.ToString())] };
        }

        var query = subquery.QueryExpression;
        if (ForOf(query) is not JsonForClause json)
            return Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"A FOR XML subquery is not modelled yet: {Text(subquery)}", at));

        // A FOR JSON subquery is JSON that an enclosing FOR JSON or constructor embeds without escaping, but only with
        // its array wrapper: WITHOUT_ARRAY_WRAPPER makes it text, escaped as a string wherever it nests, until json_query
        // makes it JSON again (both observed). Either is null when the subquery returns no rows, unless its query always
        // returns exactly one.
        var forJson = Query(query, scope);
        var shape = ForJson(forJson, json, subquery: true);
        if (shape is UnknownShape unknown)
            return Value.Unknown(unknown);
        EquatableArray<IrNullReason> empty = forJson.Rows == IrCardinality.One ? [] : [new IrNullReason(IrNullRule.EmptySubquery, at.ToString())];
        return json.Options.Any(o => o.OptionKind == JsonForClauseOptions.WithoutArrayWrapper)
            ? new Value(new JsonTextShape(shape), empty)
            : new Value(shape, empty, IsLiveJson: true);
    }

    private Value JsonQuery(FunctionCall call, BindingScope scope)
    {
        // A literal, or CASE, IIF, CHOOSE or COALESCE over JSON, is read for what it holds (ProcedureAnalyzer.JsonTracing.cs).
        var argument = JsonQueryArgument(call.Parameters[0], scope);
        if (argument.IsLiveJson || argument.Shape is UnknownShape)
            return argument;
        if (argument.Shape is JsonTextShape { Content: { } shape })
            return shape is UnknownShape unknown ? Value.Unknown(unknown) : new Value(shape, argument.NullableBecause, IsLiveJson: true);
        if (argument.JsonWhy is { } why)
            return Value.Unknown(why);

        // json_query over text (a column, a variable) makes it live JSON, but its shape is in data haruspex cannot see.
        return Value.Unknown(Unsupported(IrUnknownReason.JsonShapeUnknown, $"{Text(call)} reads JSON that is not built here, so its shape is unknown.", At(call)));
    }

    // ---- scalar functions ---------------------------------------------------------------------------------
    //
    // Observed on SQL Server 2025 RTM-CU10 and kept as tests (JsonFunctionObservationTests): a call of a function
    // returning nvarchar(max) nests as an escaped string in FOR JSON and the constructors, even when it holds JSON;
    // json_query over it, or a return type of json, nests it as JSON. FOR JSON over no rows returns NULL from the function,
    // and a key whose value is NULL is left out of FOR JSON PATH. RETURNS NULL ON NULL INPUT makes f(NULL) NULL without
    // running the body. FunctionSummaries says which bodies are read and how recursion is solved.

    private Value CallFunction(FunctionCall call, ObjectName callee, BindingScope scope)
    {
        var at = At(call);
        if (call.OverClause is not null || call.WithinGroupClause is not null)
            return Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"This form of call is not modelled: {Text(call)}", at));

        // A function's body read in a statement that reads uncommitted reads its tables so too.
        var (result, why) = (ReadUncommitted ? functions.Uncommitted : functions).Find(callee, at);
        if (result is null)
            return Value.Unknown(why!);

        var because = new List<IrNullReason>(result.Content is null ? [new IrNullReason(IrNullRule.FunctionResult, at.ToString())] : result.NullableBecause);

        // With RETURNS NULL ON NULL INPUT a null argument makes the call NULL. The arguments are read only for that.
        if (result.NullOnNullInput && call.Parameters.Any(p => Expression(p, scope).NullableBecause.Count > 0))
            because.Add(new IrNullReason(IrNullRule.NullInput, at.ToString()));

        return result switch
        {
            { Content: null, ReturnsJson: true } => new Value(result.Declared, [.. because], IsLiveJson: true),
            { Content: null } => Value.Computed(result.Declared, [.. because]),
            { ReturnsJson: true } => result.Content is UnknownShape unknown ? Value.Unknown(unknown) : new Value(result.Content, [.. because], IsLiveJson: true),
            _ => new Value(new JsonTextShape(result.Content), [.. because]),
        };
    }

    // ---- JSON constructors ----------------------------------------------------------------------------
    //
    // Every rule here was observed on SQL Server 2025 RTM-CU10 before it was relied on, and is kept as a test
    // (JsonConstructorObservationTests in the differential tests):
    // - json_object and json_objectagg keep a null value's key by default (NULL ON NULL); json_array and
    //   json_arrayagg drop a null by default (ABSENT ON NULL). Each takes the other option when it is written.
    // - A constructor, json_query, a FOR JSON subquery with its array wrapper and a json value nest as JSON in a
    //   constructor and in FOR JSON; any other string is escaped text, a FOR JSON subquery WITHOUT_ARRAY_WRAPPER
    //   included. So a constructor is live JSON, and a value keeps its shape (and its
    //   facets, while it passes through unchanged) and its nullability.
    // - Scalars are written as FOR JSON writes them (dates, times, GUIDs, bit, decimals, bigint, padded char), so
    //   they keep the kinds they have in FOR JSON.
    // - The aggregates over no rows are NULL, not [] or {}; under GROUP BY there is no group without a row, even
    //   with ROLLUP or GROUP BY (), so there they are never null.
    // - json_object writes a repeated literal key twice, and json_objectagg every repeated key: JSON.parse keeps the
    //   last. A key can be any expression; a null key is an error, not a value.
    // json_object and json_array exist from SQL Server 2022, the aggregates from 2025 (and Azure SQL). The target's
    // version is not known yet, so a constructor it lacks fails when the procedure is created, not here.

    /// <summary>A call of a JSON constructor: <c>json_object</c>, <c>json_array</c> and their aggregates.</summary>
    private static bool IsConstructor(FunctionCall call) =>
        call.CallTarget is null
        && ((BuiltInFunctions.Named(call.FunctionName.Value, FunctionForm.Scalar) is { } scalar && (scalar == ScalarFunctions.JsonObject || scalar == ScalarFunctions.JsonArray))
            || (BuiltInFunctions.Named(call.FunctionName.Value, FunctionForm.Aggregate) is { } aggregate && (aggregate == AggregateFunctions.JsonArrayAgg || aggregate == AggregateFunctions.JsonObjectAgg)));

    private Value Constructor(FunctionCall call, BindingScope scope, BuiltInFunction function)
    {
        var at = At(call);
        var name = call.FunctionName.Value;

        // Forms the server rejects or that would change what an aggregate sees (a window, DISTINCT) are not modelled.
        if (call.OverClause is not null || call.WithinGroupClause is not null || call.UniqueRowFilter != UniqueRowFilter.NotSpecified
            || call.IgnoreRespectNulls is { Count: > 0 } || call.WithArrayWrapper)
        {
            return Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"This form of {name} is not modelled: {Text(call)}", at));
        }

        // RETURNING json changes the value's SQL type, not what it writes or how it nests (observed).
        if (call.ReturnType is { Count: > 0 } returns && returns is not [SqlDataTypeReference { SqlDataTypeOption: SqlDataTypeOption.Json }])
            return Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"{name} returning anything but json is not modelled: {Text(call)}", at));

        bool? nullOnNull = call.AbsentOrNullOnNull switch
        {
            null or [] => null,
            [{ Value: var option }] when option.Equals("NULL", StringComparison.OrdinalIgnoreCase) => true,
            [{ Value: var option }] when option.Equals("ABSENT", StringComparison.OrdinalIgnoreCase) => false,
            _ => throw new InvalidOperationException($"Unexpected null option in {Text(call)}."),
        };

        if (function == ScalarFunctions.JsonObject)
            return JsonObjectConstructor(call, scope, nullOnNull ?? true);
        if (function == ScalarFunctions.JsonArray)
            return JsonArrayConstructor(call, scope, nullOnNull ?? false);
        if (function == AggregateFunctions.JsonArrayAgg)
            return ArrayAggregate(call, scope, nullOnNull ?? false);
        return ObjectAggregate(call, scope, nullOnNull ?? true);
    }

    /// <summary><c>json_object('k': v, …)</c>: an object of literal keys, never null.</summary>
    private Value JsonObjectConstructor(FunctionCall call, BindingScope scope, bool nullOnNull)
    {
        var fields = new List<FieldShape>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in call.JsonParameters)
        {
            // Keys are compared by case, as JSON compares them: 'a' and 'A' are two keys (observed).
            if (pair.JsonKeyName is not StringLiteral key)
                return Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"The key {Text(pair.JsonKeyName)} of json_object is not a literal, so the object's keys are data.", At(pair.JsonKeyName)));
            if (!keys.Add(key.Value))
                return Value.Unknown(Unsupported(IrUnknownReason.DuplicateColumn, $"The key '{key.Value}' is used more than once in json_object, which writes it twice.", At(pair.JsonKeyName)));

            var value = Expression(pair.JsonValue, scope);
            var presence = value.NullableBecause.Count > 0 && !nullOnNull ? IrPresence.OmittedWhenNull : IrPresence.Always;
            fields.Add(new FieldShape(key.Value, value.Shape, presence, value.NullableBecause));
        }

        return new Value(new ObjectShape([.. fields]), [], IsLiveJson: true);
    }

    /// <summary>
    /// <c>json_array(v, …)</c>, never null. Under NULL ON NULL every position stays, so it is a tuple. Under ABSENT ON
    /// NULL a null is dropped and the rest close up: a tuple only when no element can be null, otherwise an array of the
    /// elements' types, with as many items as the elements never null and at most one per element.
    /// </summary>
    private Value JsonArrayConstructor(FunctionCall call, BindingScope scope, bool nullOnNull)
    {
        var elements = call.Parameters.Select(p => Expression(p, scope)).ToList();
        if (nullOnNull || elements.All(e => e.NullableBecause.Count == 0))
            return new Value(new TupleShape([.. elements.Select(e => new ElementShape(e.Shape, e.NullableBecause))]), [], IsLiveJson: true);

        var variants = Distinct(elements.Select(e => e.Shape));
        var present = elements.Count(e => e.NullableBecause.Count == 0);
        return new Value(
            new ArrayShape(variants.Count == 1 ? variants[0] : new UnionShape([.. variants]), [], MinItems: present > 0 ? present : null, MaxItems: elements.Count),
            [],
            IsLiveJson: true);
    }

    /// <summary>
    /// The different shapes among <paramref name="shapes"/>, in order. Scalars are the same when they are equal (the same
    /// kind, format, SQL type and facets); any other shape is kept as it is, since telling two objects apart is not worth the
    /// risk of merging them.
    /// </summary>
    private static List<Shape> Distinct(IEnumerable<Shape> shapes)
    {
        var result = new List<Shape>();
        var seen = new HashSet<IrScalar>();
        foreach (var shape in shapes)
        {
            if (shape is ScalarShape scalar && !seen.Add(scalar.Type))
                continue;
            result.Add(shape);
        }

        return result;
    }

    /// <summary>
    /// <c>json_arrayagg(v [ORDER BY …])</c>: an array of v. A non-null result means at least one row, so it has an item
    /// unless ABSENT ON NULL can drop them all.
    /// </summary>
    private Value ArrayAggregate(FunctionCall call, BindingScope scope, bool nullOnNull)
    {
        if (call.Parameters.Count != 1)
            return Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"json_arrayagg of {call.Parameters.Count} values is not modelled: {Text(call)}", At(call)));

        var value = InAggregate(() => Expression(call.Parameters[0], scope));
        var neverEmpty = nullOnNull || value.NullableBecause.Count == 0;
        return new Value(
            new ArrayShape(value.Shape, nullOnNull ? value.NullableBecause : [], MinItems: neverEmpty ? 1 : null),
            Aggregate(call),
            IsLiveJson: true);
    }

    /// <summary>
    /// <c>json_objectagg(k: v)</c>: keys come from data, so a record of string to v. Every key is a string whatever the
    /// key expression's type, so the key is not typed; a repeated key is written each time, and JSON.parse keeps the last.
    /// </summary>
    private Value ObjectAggregate(FunctionCall call, BindingScope scope, bool nullOnNull)
    {
        if (call.JsonParameters is not [var pair])
            return Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"This form of json_objectagg is not modelled: {Text(call)}", At(call)));

        var value = InAggregate(() => Expression(pair.JsonValue, scope));
        return new Value(new RecordShape(value.Shape, nullOnNull ? value.NullableBecause : []), Aggregate(call), IsLiveJson: true);
    }

    /// <summary>
    /// Why an aggregate can be null: over no rows it is NULL, which only a query without GROUP BY can give it (with
    /// GROUP BY, no rows means no groups at all).
    /// </summary>
    private EquatableArray<IrNullReason> Aggregate(FunctionCall call) =>
        current?.GroupByClause is null && !ProvedAggregate(call) ? [new IrNullReason(IrNullRule.EmptyAggregate, At(call).ToString())] : [];

    // ---- helpers ------------------------------------------------------------------------------------

    private static UnknownShape Unsupported(IrUnknownReason reason, string detail, SourceLocation at) => new(reason, detail, at);

    private SourceLocation At(TSqlFragment fragment) => new(file, fragment.StartLine, fragment.StartColumn);

    private static string Text(SourceLocation at) => $"at {at}";

    private static string Text(TSqlFragment fragment) => FragmentText.OneLine(fragment);
}
