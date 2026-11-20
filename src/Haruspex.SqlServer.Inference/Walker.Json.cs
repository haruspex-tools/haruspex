using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using static Haruspex.SqlServer.Inference.Typer;

namespace Haruspex.SqlServer.Inference;

// JSON built here, traced to where it is read. Every rule here was observed on SQL Server 2025 RTM-CU10 before it
// was relied on, and is kept as a test (JsonTracingObservationTests in the differential tests). See README.md, "JSON built
// here, traced".
internal sealed partial class Walker
{
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
        var slot = JsonContentWritten(value, expression, at, where);

        // The place holds the text as it is written, so it keeps what the text is made of (Typer.JsonAssembled.cs).
        if (slot == JsonSlot.Empty || value.IsLiveJson || value.Pieces is not { } pieces)
            return slot;
        // A write is noted only when its text is known JSON: text that only opens with a bracket, such as an identifier quoted
        // for dynamic SQL ('[' + @name + ']'), is not JSON built as text until something reads it as JSON, where it is noted.
        if (expression is not null && JsonAssembly.Parse(pieces, at, out var built) is { } and not UnknownShape && built)
            summaries.Assembled(At(Strip(expression)), Strip(expression), JsonAssembly.Aggregates(pieces));
        return slot with { Pieces = pieces };
    }

    /// <summary>What one write puts in a traced place as JSON (<see cref="JsonWritten"/>), before what its text is made of.</summary>
    private JsonSlot JsonContentWritten(Value value, ScalarExpression? expression, SourceLocation at, string where)
    {
        if (expression is not null && Strip(expression) is NullLiteral)
            return JsonSlot.Empty;
        if (value.Shape is UnknownShape unknown)
        {
            return JsonSlot.Text(unknown with
            {
                Reason = IrUnknownReason.JsonShapeUnknown,
                Detail = $"{where} is assigned {(expression is null ? "a value" : Text(expression))} {Text(at)}, which is not known: {unknown.Detail}",
                At = at,
            });
        }
        if (value.IsLiveJson)
            return JsonSlot.Json(value.Shape);
        if (value.Shape is JsonTextShape text)
        {
            return JsonSlot.Json(text.Content ?? Unsupported(IrUnknownReason.JsonShapeUnknown,
                $"{where} is assigned JSON whose shape is not known {Text(at)}{(expression is null ? "" : $": {Text(expression)}")}.", at));
        }

        // JSON assembled as text holds its JSON as text, but stays a string where it is read without json_query.
        if (Assembled(value, at) is { } assembled)
            return assembled is UnknownShape notKnown ? JsonSlot.Text(notKnown) : JsonSlot.Assembled(assembled);

        if (expression is not null && LiteralOf(expression) is ({ } literal and StringLiteral, false))
        {
            var parsed = JsonLiterals.Parse(literal.Value, literal is StringLiteral { IsNational: true }, At(literal));
            return parsed is UnknownShape notJson ? JsonSlot.Text(notJson with { Detail = $"{where} is assigned a literal {Text(at)}: {notJson.Detail}" }) : JsonSlot.Json(parsed);
        }

        if (value.JsonWhy is { } why)
            return JsonSlot.Text(why);
        var what = expression is null ? "a value" : Text(expression);
        return JsonSlot.Text(Unsupported(IrUnknownReason.JsonShapeUnknown, $"{where} is assigned {what} {Text(at)}, which is not JSON built here.", at));
    }

    // ---- table variables ----------------------------------------------------------------------------------------------
    //
    // A table variable is its declaration, but what its nvarchar(max) columns hold as JSON is what the body writes to
    // them, kept in FlowState.Json under a ColumnSlot. Only this body can write one; every statement that does is read here:
    // INSERT (VALUES or a query), UPDATE's SET, OUTPUT … INTO; INSERT … EXEC and MERGE write values not read, so the columns
    // they may write are not JSON known here.

    /// <summary>The traced columns of a table variable, by position in its declaration; empty when none is.</summary>
    private static IEnumerable<(int Position, ColumnSlot Key)> TracedColumns(FlowState state, LocalName name, IReadOnlyList<string> columns) =>
        columns.Select((c, i) => (i, new ColumnSlot(name, c))).Where(c => state.Json.ContainsKey(c.Item2));

    /// <summary>The positions an insert's values go to: its column list's, or every column but an identity column.</summary>
    private List<int>? TableVariableTargets(LocalName name, IList<ColumnReferenceExpression> listed)
    {
        if (this.names.TableVariable(name)?.Declaration is not { } table)
            return null;
        var names = table.Columns.Select(c => c.Name).ToList();
        var targets = listed.Count == 0
            ? [.. Enumerable.Range(0, names.Count).Where(i => !table.Columns[i].IsIdentity)]
            : Binder.Written(listed, names).ToList();
        return targets.Any(i => i < 0) ? null : targets;
    }

    /// <summary>Rows written to a table variable: each traced column joins with what they put in it.</summary>
    /// <param name="how">How the statement writes it, when its values are not read.</param>
    private FlowState TableVariableWritten(FlowState state, LocalName name, List<int>? targets, IReadOnlyList<IReadOnlyList<Value>>? rows, IReadOnlyList<IReadOnlyList<ScalarExpression?>>? expressions, SourceLocation at, string how = "by a statement whose values are not read")
    {
        if (names.TableVariable(name)?.Columns is not { } columns)
            return state;
        foreach (var (position, key) in TracedColumns(state, name, columns).ToList())
        {
            var written = targets is null || rows is null
                ? JsonSlot.Text(Unsupported(IrUnknownReason.JsonShapeUnknown, $"{key} is written {Text(at)} {how}.", at))
                : JsonRows(rows, expressions, targets.IndexOf(position), at, key.ToString());
            state = state.WithJson(key, JsonSlot.Join(state.Json[key], written));
        }

        return state;
    }

    private FlowState TableVariableInsert(InsertStatement insert, LocalName name, FlowState state)
    {
        var spec = insert.InsertSpecification;
        var at = At(insert);
        if (names.TableVariable(name)?.Columns is not { } columns || !TracedColumns(state, name, columns).Any())
            return state;
        var targets = spec.InsertSource is ValuesInsertSource { IsDefaultValues: true } ? [] : TableVariableTargets(name, spec.Columns);
        if (targets is null)
            return TableVariableWritten(state, name, null, null, null, at);
        var rows = typer.WithCtes(insert.WithCtesAndXmlNamespaces, Reading(state), scope => InsertedRows(spec.InsertSource, targets.Count, at, scope), why => [[.. targets.Select(_ => Value.Unknown(why))]]);
        return TableVariableWritten(state, name, targets, rows, rows is null ? null : InsertedExpressions(spec.InsertSource, rows.Count), at);
    }

    /// <summary>
    /// The table variables a write through a CTE or derived table may change underneath (every one its target and WITH name),
    /// each traced column no longer known as JSON.
    /// </summary>
    private FlowState Underneath(FlowState state, TableReference target, WithCtesAndXmlNamespaces? with, SourceLocation at)
    {
        TSqlFragment?[] written = [target, with];
        foreach (var table in written.OfType<TSqlFragment>().SelectMany(names.Declarations.TableVariablesIn).Distinct())
            state = TableVariableWritten(state, new LocalName(table.Name), null, null, null, at, "through a CTE or derived table, which is not followed");
        return state;
    }

    // ---- loops --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// <paramref name="next"/>, with the JSON that changed since <paramref name="head"/> widened to unknown content naming the
    /// loop at <paramref name="at"/>: a traced place's slot, and a temporary table column's JSON text. Unknown content joins to
    /// itself, so a loop whose only change is JSON that grows reaches a fixed point.
    /// </summary>
    private static FlowState Widened(FlowState head, FlowState next, SourceLocation at)
    {
        UnknownShape Why(string where) =>
            new(IrUnknownReason.JsonShapeUnknown, $"{where} holds JSON that changes at every pass of the loop {Text(at)}, so its shape is not followed.", at);

        foreach (var (place, slot) in next.Json)
        {
            if (!head.Json.TryGetValue(place, out var before) || before != slot)
                next = next.WithJson(place, JsonSlot.Text(Why(place.ToString())));
        }

        foreach (var (name, table) in next.Temps)
        {
            if (table is not KnownTable known || head.Temp(name) is not KnownTable old || old.Columns.Count != known.Columns.Count)
                continue;
            var columns = known.Columns.Select((c, i) =>
            {
                var was = old.Columns[i];
                if (c.Json is { } slot && slot != was.Json)
                    c = c with { Json = JsonSlot.Text(Why($"{name}.{c.Name}")) };
                if (c.Value.Shape is JsonTextShape && c.Value != was.Value)
                    c = c with { Value = c.Value with { Shape = new JsonTextShape(Why($"{name}.{c.Name}")) } };
                return c;
            }).ToList();
            next = next.WithTemp(name, known with { Columns = [.. columns] });
        }

        return next;
    }
}
