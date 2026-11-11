using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// JSON built here, traced to where it is read (slice 6l). Every rule here was observed on SQL Server 2025 RTM-CU10 before it
// was relied on, and is kept as a test (JsonTracingObservationTests in the differential tests). See README.md, "JSON built
// here, traced".
internal sealed partial class ProcedureAnalyzer
{
    /// <summary>
    /// Whether a place of this declared value keeps JSON text written to it unchanged: <c>nvarchar(max)</c>. A shorter type cuts
    /// it short without an error, and <c>varchar</c> may change characters its code page lacks (observed), so neither is traced.
    /// </summary>
    private static bool Traced(Shape declared) =>
        declared is ScalarShape { Type.Sql: "nvarchar(max)" };

    private static bool Traced(Value declared) => !declared.IsLiveJson && Traced(declared.Shape);

    /// <summary>
    /// What one write puts in a traced place (<see cref="JsonSlot"/>): live JSON or JSON text built here its content, a JSON
    /// literal its parsed shape, NULL nothing; anything else may not be JSON, and the slot says which write it was.
    /// </summary>
    /// <param name="value">The value written.</param>
    /// <param name="expression">Its expression, when it has one in the source.</param>
    /// <param name="at">The write.</param>
    /// <param name="where">The place, for the message.</param>
    private JsonSlot JsonWritten(Value value, ScalarExpression? expression, SourceLocation at, string where)
    {
        if (expression is not null && Strip(expression) is NullLiteral)
            return JsonSlot.Empty;
        if (value.Shape is UnknownShape unknown)
        {
            return JsonSlot.Text(unknown with
            {
                Reason = IrUnknownReasons.JsonShapeUnknown,
                Detail = $"{where} is assigned {(expression is null ? "a value" : Text(expression))} {Text(at)}, which is not known: {unknown.Detail}",
                At = at,
            });
        }
        if (value.IsLiveJson)
            return JsonSlot.Json(value.Shape);
        if (value.Shape is JsonTextShape text)
        {
            return JsonSlot.Json(text.Content ?? Unsupported(IrUnknownReasons.JsonShapeUnknown,
                $"{where} is assigned JSON whose shape is not known {Text(at)}{(expression is null ? "" : $": {Text(expression)}")}.", at));
        }

        if (expression is not null && LiteralOf(expression) is ({ } literal and StringLiteral, false))
        {
            var parsed = JsonLiterals.Parse(literal.Value, At(literal));
            return parsed is UnknownShape notJson ? JsonSlot.Text(notJson with { Detail = $"{where} is assigned a literal {Text(at)}: {notJson.Detail}" }) : JsonSlot.Json(parsed);
        }

        if (value.JsonWhy is { } why)
            return JsonSlot.Text(why);
        var what = expression is null ? "a value" : Text(expression);
        return JsonSlot.Text(Unsupported(IrUnknownReasons.JsonShapeUnknown, $"{where} is assigned {what} {Text(at)}, which is not JSON built here.", at));
    }

    // ---- table variables ----------------------------------------------------------------------------------------------
    //
    // A table variable is its declaration (slice 6), but what its nvarchar(max) columns hold as JSON is what the body writes to
    // them, kept in FlowState.Json under "@t.column". Only this body can write one; every statement that does is read here:
    // INSERT (VALUES or a query), UPDATE's SET, OUTPUT … INTO; INSERT … EXEC and MERGE write values not read, so the columns
    // they may write are not JSON known here.

    /// <summary>The traced columns of a table variable, by position in its declaration; empty when none is.</summary>
    private static IEnumerable<(int Position, string Key)> TracedColumns(FlowState state, string name, IReadOnlyList<string> columns) =>
        columns.Select((c, i) => (i, $"{name}.{c}")).Where(c => state.Json.ContainsKey(c.Item2));

    /// <summary>The positions an insert's values go to: its column list's, or every column but an identity column.</summary>
    private List<int>? TableVariableTargets(string name, IList<ColumnReferenceExpression> listed)
    {
        if (!tableVariableDefinitions.TryGetValue(name, out var table))
            return null;
        var names = table.Columns.Select(c => c.Name).ToList();
        var targets = listed.Count == 0
            ? [.. Enumerable.Range(0, names.Count).Where(i => !table.Columns[i].IsIdentity)]
            : listed.Select(c => names.FindIndex(n => n.Equals(c.MultiPartIdentifier.Identifiers[^1].Value, StringComparison.OrdinalIgnoreCase))).ToList();
        return targets.Any(i => i < 0) ? null : targets;
    }

    /// <summary>Rows written to a table variable: each traced column joins with what they put in it.</summary>
    /// <param name="how">How the statement writes it, when its values are not read.</param>
    private FlowState TableVariableWritten(FlowState state, string name, List<int>? targets, IReadOnlyList<IReadOnlyList<Value>>? rows, IReadOnlyList<IReadOnlyList<ScalarExpression?>>? expressions, SourceLocation at, string how = "by a statement whose values are not read")
    {
        if (locals!.TableVariableColumns(name) is not { } columns)
            return state;
        foreach (var (position, key) in TracedColumns(state, name, columns).ToList())
        {
            var written = targets is null || rows is null
                ? JsonSlot.Text(Unsupported(IrUnknownReasons.JsonShapeUnknown, $"{key} is written {Text(at)} {how}.", at))
                : JsonRows(rows, expressions, targets.IndexOf(position), at, key);
            state = state.WithJson(key, JsonSlot.Join(state.Json[key], written));
        }

        return state;
    }

    private FlowState TableVariableInsert(InsertStatement insert, string name, FlowState state)
    {
        var spec = insert.InsertSpecification;
        var at = At(insert);
        if (locals!.TableVariableColumns(name) is not { } columns || !TracedColumns(state, name, columns).Any())
            return state;
        var targets = spec.InsertSource is ValuesInsertSource { IsDefaultValues: true } ? [] : TableVariableTargets(name, spec.Columns);
        if (targets is null)
            return TableVariableWritten(state, name, null, null, null, at);
        var rows = WithCtes(insert.WithCtesAndXmlNamespaces, () => InsertedRows(spec.InsertSource, targets.Count, at), why => [[.. targets.Select(_ => Value.Unknown(why))]]);
        return TableVariableWritten(state, name, targets, rows, rows is null ? null : InsertedExpressions(spec.InsertSource, rows.Count), at);
    }

    /// <summary>
    /// The table variables a write through a CTE or derived table may change underneath (every one its target and WITH name),
    /// each traced column no longer known as JSON.
    /// </summary>
    private FlowState Underneath(FlowState state, TableReference target, WithCtesAndXmlNamespaces? with, SourceLocation at)
    {
        var found = new TableVariables();
        target.Accept(found);
        with?.Accept(found);
        foreach (var name in found.Names.Distinct(StringComparer.OrdinalIgnoreCase))
            state = TableVariableWritten(state, name, null, null, null, at, "through a CTE or derived table, which is not followed");
        return state;
    }

    private sealed class TableVariables : TSqlFragmentVisitor
    {
        public List<string> Names { get; } = [];

        public override void Visit(VariableTableReference node) => Names.Add(node.Variable.Name);
    }

    // ---- json_query ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// The expression json_query is reading, while it is read: a CASE, IIF, CHOOSE or COALESCE there holds JSON whether or not the
    /// server folds it to one result, so its results' JSON is joined (<see cref="Results"/>, <see cref="Coalesce"/>).
    /// </summary>
    private TSqlFragment? jsonQueryArgument;

    /// <summary>
    /// What <c>json_query</c>'s argument holds: a literal that is a JSON document its exact shape, as JSON text never null; a
    /// literal that is not, an unknown saying why (it is an error, 13609, observed); anything else its value, a CASE, IIF,
    /// CHOOSE or COALESCE read as JSON (<see cref="jsonQueryArgument"/>).
    /// </summary>
    private Value JsonQueryArgument(ScalarExpression argument, BindingScope scope)
    {
        if (LiteralOf(argument) is ({ } literal and StringLiteral, false))
        {
            var parsed = JsonLiterals.Parse(literal.Value, At(literal));
            return parsed is UnknownShape unknown ? Value.Unknown(unknown) : new Value(new JsonTextShape(parsed), []);
        }

        var outer = jsonQueryArgument;
        jsonQueryArgument = Strip(argument);
        try
        {
            return Expression(argument, scope);
        }
        finally
        {
            jsonQueryArgument = outer;
        }
    }

    /// <summary>
    /// Whether an expression is built JSON whose SQL type is <c>nvarchar(max)</c>, so that a column <c>SELECT … INTO</c> makes of
    /// it holds the JSON as text: a FOR JSON subquery, or a constructor not <c>RETURNING json</c>, which makes a <c>json</c> column
    /// (observed: Select_into_makes_nvarchar_max_of_built_json_and_json_of_returning_json). Anything else live, such as
    /// json_query, is not read (its type follows its argument's).
    /// </summary>
    private static bool BuiltAsText(ScalarExpression expression) => Strip(expression) switch
    {
        ScalarSubquery subquery => ForOf(subquery.QueryExpression) is JsonForClause,
        FunctionCall { CallTarget: null } call => IsConstructor(call) && call.ReturnType is not { Count: > 0 },
        _ => false,
    };
}
