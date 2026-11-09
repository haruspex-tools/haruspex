using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>What inference found in one procedure.</summary>
/// <param name="Parameters">Each declared parameter and its type.</param>
/// <param name="Results">The result sets in order, or null when a statement makes them impossible to list.</param>
/// <param name="ResultsUnknown">Why the results cannot be listed, when <paramref name="Results"/> is null.</param>
internal sealed record ProcedureAnalysis(
    IReadOnlyList<ParameterShape> Parameters,
    IReadOnlyList<ResultShape>? Results,
    UnknownShape? ResultsUnknown);

internal sealed record ParameterShape(string Name, Shape Type, bool HasDefault, bool IsOutput);

/// <param name="AbsentBecause">Why the result can arrive with no value at all (see <see cref="IrResult"/>).</param>
internal sealed record ResultShape(IrEncoding Encoding, Shape Shape, IReadOnlyList<IrNullReason> AbsentBecause);

/// <summary>
/// Infers the result sets of one procedure. Every construct it does not model becomes an
/// <see cref="UnknownShape"/> with a reason, so what it does claim can be trusted.
/// </summary>
/// <remarks>
/// Which table and column a reference reads is the <see cref="Binder"/>'s job (Haruspex.SqlServer.Semantics);
/// what that means for a value's shape and nullability is decided here. Slice 2b split the two out of this one
/// class, so that lint, lineage and diff can bind without inferring.
/// </remarks>
internal sealed class ProcedureAnalyzer
{
    private readonly DatabaseModel model;
    private readonly Binder binder;
    private readonly string file;

    /// <summary>The query whose select list is being read, for an aggregate to know whether it has GROUP BY.</summary>
    private QuerySpecification? current;

    public ProcedureAnalyzer(DatabaseModel model, string file)
    {
        this.model = model;
        binder = new Binder(model, file);
        this.file = file;
    }

    public ProcedureAnalysis Analyse(ProcedureStatementBody procedure)
    {
        var parameters = procedure.Parameters
            .Select(p => new ParameterShape(
                p.VariableName.Value,
                SqlTypes.ToShape(model, SqlType.From(p.DataType), At(p)),
                HasDefault: p.Value is not null,
                IsOutput: p.Modifier == ParameterModifier.Output))
            .ToList();

        // A CLR procedure's body is .NET code, so its results cannot be known from the project.
        if (procedure.MethodSpecifier is not null || procedure.StatementList is null)
        {
            return new ProcedureAnalysis(parameters, null, new UnknownShape(
                IrUnknownReasons.ExternalCode,
                "This is a CLR procedure: its results come from .NET code, which is not in the project.",
                At(procedure)));
        }

        var results = new List<ResultShape>();
        var blocker = Walk(procedure.StatementList.Statements, results);
        return blocker is null
            ? new ProcedureAnalysis(parameters, results, null)
            : new ProcedureAnalysis(parameters, null, blocker);
    }

    // ---- statements ---------------------------------------------------------------------------------

    /// <summary>
    /// Collects the result sets of a statement list. Returns the first statement that makes the list of
    /// results unknowable, because it could return results of its own or decide which others run.
    /// </summary>
    private UnknownShape? Walk(IEnumerable<TSqlStatement> statements, List<ResultShape> results)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case PredicateSetStatement set when IsHarmless(set):
                    break;

                // These never return a result set. Variables are not tracked yet, so a later reference to one
                // is an unknown expression rather than a wrong type.
                case DeclareVariableStatement:
                case DeclareTableVariableStatement:
                case SetVariableStatement:
                    break;
                case BeginEndBlockStatement block:
                    if (Walk(block.StatementList.Statements, results) is { } blocker)
                        return blocker;
                    break;
                case SelectStatement select:
                    if (Select(select) is { } result)
                        results.Add(result);
                    break;
                default:
                    var keyword = statement.ScriptTokenStream[statement.FirstTokenIndex].Text.ToUpperInvariant();
                    return new UnknownShape(
                        IrUnknownReasons.UnsupportedStatement,
                        $"{keyword} is not modelled yet, so the procedure's result sets cannot be listed.",
                        At(statement));
            }
        }

        return null;
    }

    /// <summary>
    /// Session options known not to change which result sets run or their shape. Anything else is not modelled:
    /// NOEXEC, FMTONLY and PARSEONLY stop the rows from being returned at all, and turning off ANSI_WARNINGS
    /// or CONCAT_NULL_YIELDS_NULL changes where expressions produce null.
    /// </summary>
    private static readonly SetOptions AnyValue = SetOptions.NoCount | SetOptions.XactAbort;

    private static readonly SetOptions OnlyOn =
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
        var encoding = select.QueryExpression.ForClause is JsonForClause ? IrEncoding.Json : IrEncoding.Rowset;

        // A top-level FOR JSON query that matches no rows returns no row at all, rather than [] or {}. A
        // rowset that matches no rows is still a result set, with no rows.
        IReadOnlyList<IrNullReason> absent = encoding == IrEncoding.Json ? [new IrNullReason(IrNullRules.NoRows, at.ToString())] : [];

        if (select.WithCtesAndXmlNamespaces is not null)
            return new ResultShape(encoding, Unsupported(IrUnknownReasons.UnsupportedQuery, "A common table expression (WITH) is not modelled yet.", at), absent);
        if (select.QueryExpression is not QuerySpecification query)
            return new ResultShape(encoding, Unsupported(IrUnknownReasons.UnsupportedQuery, "A UNION, INTERSECT, EXCEPT or parenthesised query is not modelled yet.", at), absent);

        var row = Query(query, parent: null);
        return query.ForClause switch
        {
            JsonForClause json => new ResultShape(IrEncoding.Json, ForJson(row, json, at), absent),
            null => new ResultShape(IrEncoding.Rowset, Rowset(row), absent),
            _ => new ResultShape(IrEncoding.Rowset, Unsupported(IrUnknownReasons.UnsupportedQuery, $"{Text(query.ForClause)} is not modelled yet.", at), []),
        };
    }

    // ---- queries ------------------------------------------------------------------------------------

    private sealed record Row(IReadOnlyList<RowColumn> Columns, SourceLocation? Star);

    private Row Query(QuerySpecification query, BindingScope? parent)
    {
        var scope = binder.BindFrom(query, parent);

        // WHERE, GROUP BY and HAVING are not read. They can only remove rows or make values non-null, so
        // ignoring them can leave a field nullable that is not, but never the reverse.
        var columns = new List<RowColumn>();
        SourceLocation? star = null;
        var outer = current;
        current = query;
        try
        {
            foreach (var element in query.SelectElements)
            {
                switch (element)
                {
                    case SelectScalarExpression scalar:
                        var name = scalar.ColumnName?.Value
                            ?? (scalar.Expression as ColumnReferenceExpression)?.MultiPartIdentifier?.Identifiers[^1].Value;
                        columns.Add(new RowColumn(name, Expression(scalar.Expression, scope), At(scalar)));
                        break;
                    case SelectStarExpression:
                        star ??= At(element);
                        break;
                }
            }
        }
        finally
        {
            current = outer;
        }

        return new Row(columns, star);
    }

    private static Shape Rowset(Row row)
    {
        if (row.Star is { } star)
            return Unsupported(IrUnknownReasons.UnsupportedQuery, "SELECT * is not expanded yet.", star);
        if (row.Columns.FirstOrDefault(c => string.IsNullOrEmpty(c.Name)) is { } unnamed)
            return Unsupported(IrUnknownReasons.UnnamedColumn, $"The column {Text(unnamed.At)} has no name.", unnamed.At);

        // Drivers key a row by column name, and the mssql driver turns a repeated name into an array, so a
        // repeated name changes the row's shape.
        var duplicate = row.Columns.GroupBy(c => c.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            return Unsupported(IrUnknownReasons.DuplicateColumn, $"The column name '{duplicate.Key}' is used more than once.", duplicate.Skip(1).First().At);

        var fields = row.Columns.Select(c => new FieldShape(
            c.Name!,
            // A rowset column delivers JSON as text: the driver sees an nvarchar.
            c.Value.IsLiveJson ? new JsonTextShape() : c.Value.Shape,
            IrPresence.Always,
            c.Value.NullableBecause));
        return new ArrayShape(new ObjectShape([.. fields]));
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
    private Shape ForJson(Row row, JsonForClause clause, SourceLocation at)
    {
        var options = clause.Options.Select(o => o.OptionKind).ToHashSet();
        if (options.Contains(JsonForClauseOptions.Auto))
            return Unsupported(IrUnknownReasons.UnsupportedQuery, "FOR JSON AUTO is not modelled yet.", At(clause));
        if (options.Contains(JsonForClauseOptions.Root))
            return Unsupported(IrUnknownReasons.UnsupportedQuery, "FOR JSON ... ROOT is not modelled yet.", At(clause));
        if (row.Star is { } star)
            return Unsupported(IrUnknownReasons.UnsupportedQuery, "SELECT * is not expanded yet.", star);

        var root = new JsonNode();
        foreach (var column in row.Columns)
        {
            // SQL Server rejects an unnamed column in FOR JSON, so the procedure fails rather than returning
            // anything; it is reported rather than modelled.
            if (string.IsNullOrEmpty(column.Name))
                return Unsupported(IrUnknownReasons.UnnamedColumn, $"The column {Text(column.At)} has no name, which FOR JSON rejects.", column.At);

            var path = column.Name.Split('.');
            if (path.Any(p => p.Length == 0))
                return Unsupported(IrUnknownReasons.UnsupportedQuery, $"The alias '{column.Name}' is not a valid FOR JSON path.", column.At);

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

        // WITHOUT_ARRAY_WRAPPER is claimed as one object although nothing here proves the query returns at most
        // one row (WHERE is not read; recognising single-row queries is a known gap). The claim is accepted
        // because it cannot be silently wrong: several rows produce "{...},{...}", which is not valid JSON, so
        // the caller's parse fails loudly before any schema sees it. No rows are covered by AbsentBecause.
        return options.Contains(JsonForClauseOptions.WithoutArrayWrapper) ? shape : new ArrayShape(shape);
    }

    private static UnknownShape KeyConflict(RowColumn column) =>
        new(IrUnknownReasons.DuplicateColumn, $"The alias '{column.Name}' clashes with another key in the same JSON object.", column.At);

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
                ? new FieldShape(key, nested, IrPresence.OmittedWhenNull, [new IrNullReason(IrNullRules.AllFieldsNull, at.ToString())])
                : new FieldShape(key, nested, IrPresence.Always, []));
        }

        return new ObjectShape(fields);
    }

    // ---- expressions --------------------------------------------------------------------------------

    private Value Expression(ScalarExpression expression, BindingScope scope) => expression switch
    {
        ColumnReferenceExpression column => Column(column, scope),
        ParenthesisExpression parenthesis => Expression(parenthesis.Expression, scope),
        ScalarSubquery subquery => Subquery(subquery, scope),
        FunctionCall { CallTarget: null } call when IsConstructor(call) => Constructor(call, scope),
        FunctionCall { CallTarget: null, Parameters.Count: 1 } call
            when call.FunctionName.Value.Equals("json_query", StringComparison.OrdinalIgnoreCase) => JsonQuery(call, scope),
        _ => Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedExpression, $"The expression {Text(expression)} is not modelled yet.", At(expression))),
    };

    private Value Column(ColumnReferenceExpression column, BindingScope scope) => binder.BindColumn(column, scope) switch
    {
        BoundColumn bound => FromColumn(bound),
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
        if (!SchemaRules.IsNeverNull(model, table, column))
            because.Add(new IrNullReason(IrNullRules.NullableColumn, $"{table.Name}.{column.Name}"));

        // The optional side of an outer join has no row when nothing matches, so every column read from it can
        // be null, whatever its declaration says.
        if (source.OptionalSideOf is { } optional)
            because.Add(new IrNullReason(IrNullRules.OuterJoin, optional.ToString()));

        // A json column nests as JSON inside FOR JSON and the constructors (observed), but what it holds is data.
        // Through a driver it is text, which Rowset makes of any live value.
        if (!column.IsComputed && SchemaRules.ResolveType(model, column.Type)?.BuiltIn.Name == "json")
        {
            return new Value(
                Unsupported(IrUnknownReasons.JsonShapeUnknown, $"{table.Name}.{column.Name} is a json column: its shape is in the data, not the schema.", column.At),
                because,
                IsLiveJson: true);
        }

        return new Value(ColumnShape(table, column), because);
    }

    /// <summary>A column's shape when it is not null.</summary>
    private Shape ColumnShape(Table table, Column column) => column.IsComputed
        ? new UnknownShape(IrUnknownReasons.UnsupportedSqlType, $"{table.Name}.{column.Name} is a computed column, which is not modelled yet.", column.At)
        : SqlTypes.ToShape(SchemaRules.ColumnFacts(model, table, column), column.Type, column.At);

    /// <summary>
    /// The IR's reason for a binding problem. A table changed after its definition was reported as an unsupported
    /// source before binding had a layer of its own, and still is; a reference form the binder does not model is
    /// an unsupported expression.
    /// </summary>
    private static UnknownShape Unknown(BindingProblem problem) => new(
        problem.Kind switch
        {
            BindingProblemKind.UnsupportedSource or BindingProblemKind.TableRedefined => IrUnknownReasons.UnsupportedSource,
            BindingProblemKind.UnresolvedTable => IrUnknownReasons.UnresolvedTable,
            BindingProblemKind.UnresolvedColumn => IrUnknownReasons.UnresolvedColumn,
            BindingProblemKind.UnsupportedReference => IrUnknownReasons.UnsupportedExpression,
            _ => throw new ArgumentOutOfRangeException(nameof(problem), problem.Kind, "Unknown binding problem."),
        },
        problem.Detail,
        problem.At);

    private Value Subquery(ScalarSubquery subquery, BindingScope scope)
    {
        var at = At(subquery);

        // A subquery returning one JSON value, typically a correlated aggregate, keeps it live: observed for a
        // constructor, an aggregate, json_query, a json value and a FOR JSON subquery inside it. With no rows it is
        // null; more than one row is an error, not a value. Any other subquery is not modelled.
        if (subquery.QueryExpression is QuerySpecification { ForClause: null } plain)
        {
            var row = Query(plain, scope);
            if (row is { Star: null, Columns: [{ Value.IsLiveJson: true } only] })
                return new Value(only.Value.Shape, [.. only.Value.NullableBecause, new IrNullReason(IrNullRules.EmptySubquery, at.ToString())], IsLiveJson: true);
        }

        if (subquery.QueryExpression is not QuerySpecification { ForClause: JsonForClause json } query)
            return Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedExpression, $"A subquery that returns neither FOR JSON nor one JSON value is not modelled yet: {Text(subquery)}", at));

        // A FOR JSON subquery is JSON that an enclosing FOR JSON embeds without escaping. It is null when the
        // subquery returns no rows, which nothing here can rule out.
        var shape = ForJson(Query(query, scope), json, at);
        return shape is UnknownShape unknown
            ? Value.Unknown(unknown)
            : new Value(shape, [new IrNullReason(IrNullRules.EmptySubquery, at.ToString())], IsLiveJson: true);
    }

    private Value JsonQuery(FunctionCall call, BindingScope scope)
    {
        var argument = Expression(call.Parameters[0], scope);
        if (argument.IsLiveJson || argument.Shape is UnknownShape)
            return argument;

        // json_query over text (a column, a variable) makes it live JSON, but its shape is in data we cannot see.
        return Value.Unknown(Unsupported(IrUnknownReasons.JsonShapeUnknown, $"{Text(call)} reads JSON that is not built here, so its shape is unknown.", At(call)));
    }

    // ---- JSON constructors ----------------------------------------------------------------------------
    //
    // Every rule here was observed on SQL Server 2025 RTM-CU10 before it was relied on, and is kept as a test
    // (JsonConstructorObservationTests in the differential tests):
    // - json_object and json_objectagg keep a null value's key by default (NULL ON NULL); json_array and
    //   json_arrayagg drop a null by default (ABSENT ON NULL). Each takes the other option when it is written.
    // - A constructor, json_query, a FOR JSON subquery and a json value nest as JSON in a constructor and in FOR
    //   JSON; any other string is escaped text. So a constructor is live JSON, and a value keeps its shape (and its
    //   facets, while it passes through unchanged) and its nullability.
    // - Scalars are written as FOR JSON writes them (dates, times, GUIDs, bit, decimals, bigint, padded char), so
    //   they keep the kinds they have in FOR JSON.
    // - The aggregates over no rows are NULL, not [] or {}; under GROUP BY there is no group without a row, even
    //   with ROLLUP or GROUP BY (), so there they are never null.
    // - json_object writes a repeated literal key twice, and json_objectagg every repeated key: JSON.parse keeps the
    //   last. A key can be any expression; a null key is an error, not a value.
    // json_object and json_array exist from SQL Server 2022, the aggregates from 2025 (and Azure SQL). The target's
    // version is not known yet, so a constructor it lacks fails when the procedure is created, not here.

    private static readonly HashSet<string> Constructors = new(StringComparer.OrdinalIgnoreCase)
    {
        "json_object", "json_array", "json_arrayagg", "json_objectagg",
    };

    private static bool IsConstructor(FunctionCall call) => Constructors.Contains(call.FunctionName.Value);

    private Value Constructor(FunctionCall call, BindingScope scope)
    {
        var at = At(call);
        var name = call.FunctionName.Value;

        // Forms the server rejects or that would change what an aggregate sees (a window, DISTINCT) are not modelled.
        if (call.OverClause is not null || call.WithinGroupClause is not null || call.UniqueRowFilter != UniqueRowFilter.NotSpecified
            || call.IgnoreRespectNulls is { Count: > 0 } || call.WithArrayWrapper)
            return Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedExpression, $"This form of {name} is not modelled: {Text(call)}", at));

        // RETURNING json changes the value's SQL type, not what it writes or how it nests (observed).
        if (call.ReturnType is { Count: > 0 } returns && returns is not [SqlDataTypeReference { SqlDataTypeOption: SqlDataTypeOption.Json }])
            return Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedExpression, $"{name} returning anything but json is not modelled: {Text(call)}", at));

        bool? nullOnNull = call.AbsentOrNullOnNull switch
        {
            null or [] => null,
            [{ Value: var option }] when option.Equals("NULL", StringComparison.OrdinalIgnoreCase) => true,
            [{ Value: var option }] when option.Equals("ABSENT", StringComparison.OrdinalIgnoreCase) => false,
            _ => throw new InvalidOperationException($"Unexpected null option in {Text(call)}."),
        };

        if (name.Equals("json_object", StringComparison.OrdinalIgnoreCase))
            return JsonObjectConstructor(call, scope, nullOnNull ?? true);
        if (name.Equals("json_array", StringComparison.OrdinalIgnoreCase))
            return JsonArrayConstructor(call, scope, nullOnNull ?? false);
        if (name.Equals("json_arrayagg", StringComparison.OrdinalIgnoreCase))
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
                return Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedExpression, $"The key {Text(pair.JsonKeyName)} of json_object is not a literal, so the object's keys are data.", At(pair.JsonKeyName)));
            if (!keys.Add(key.Value))
                return Value.Unknown(Unsupported(IrUnknownReasons.DuplicateColumn, $"The key '{key.Value}' is used more than once in json_object, which writes it twice.", At(pair.JsonKeyName)));

            var value = Expression(pair.JsonValue, scope);
            var presence = value.NullableBecause.Count > 0 && !nullOnNull ? IrPresence.OmittedWhenNull : IrPresence.Always;
            fields.Add(new FieldShape(key.Value, value.Shape, presence, value.NullableBecause));
        }

        return new Value(new ObjectShape(fields), [], IsLiveJson: true);
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
            new ArrayShape(variants.Count == 1 ? variants[0] : new UnionShape(variants), MinItems: present > 0 ? present : null, MaxItems: elements.Count),
            [],
            IsLiveJson: true);
    }

    /// <summary>
    /// The different shapes among <paramref name="shapes"/>, in order. Scalars are the same when the IR writes them the
    /// same; any other shape is kept as it is, since telling two objects apart is not worth the risk of merging them.
    /// </summary>
    private static List<Shape> Distinct(IEnumerable<Shape> shapes)
    {
        var result = new List<Shape>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var shape in shapes)
        {
            if (shape is ScalarShape scalar && !seen.Add(IrJsonWriter.WriteType(scalar.Type)))
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
            return Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedExpression, $"json_arrayagg of {call.Parameters.Count} values is not modelled: {Text(call)}", At(call)));

        var value = Expression(call.Parameters[0], scope);
        var neverEmpty = nullOnNull || value.NullableBecause.Count == 0;
        return new Value(
            new ArrayShape(value.Shape, nullOnNull ? value.NullableBecause : null, MinItems: neverEmpty ? 1 : null),
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
            return Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedExpression, $"This form of json_objectagg is not modelled: {Text(call)}", At(call)));

        var value = Expression(pair.JsonValue, scope);
        return new Value(new RecordShape(value.Shape, nullOnNull ? value.NullableBecause : []), Aggregate(call), IsLiveJson: true);
    }

    /// <summary>
    /// Why an aggregate can be null: over no rows it is NULL, which only a query without GROUP BY can give it (with
    /// GROUP BY, no rows means no groups at all).
    /// </summary>
    private IReadOnlyList<IrNullReason> Aggregate(FunctionCall call) =>
        current?.GroupByClause is null ? [new IrNullReason(IrNullRules.EmptyAggregate, At(call).ToString())] : [];

    // ---- helpers ------------------------------------------------------------------------------------

    private static UnknownShape Unsupported(string reason, string detail, SourceLocation at) => new(reason, detail, at);

    private SourceLocation At(TSqlFragment fragment) => new(file, fragment.StartLine, fragment.StartColumn);

    private static string Text(SourceLocation at) => $"at {at}";

    private static string Text(TSqlFragment fragment) => FragmentText.OneLine(fragment);
}
