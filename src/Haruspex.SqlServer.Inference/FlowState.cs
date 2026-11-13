using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The state of a procedure at a point on its paths: each temporary table, each variable's value, and the result sets
/// returned so far. Immutable, so a branch can start from the state before it and both branches' states can be joined.
/// </summary>
internal sealed record FlowState
{
    private FlowState(EquatableMap<LocalName, TempTable> temps, EquatableMap<LocalName, Value> variables, ResultTrack results, bool readUncommitted, EquatableMap<JsonSlotKey, JsonSlot> json)
    {
        Temps = temps;
        Variables = variables;
        Results = results;
        ReadUncommitted = readUncommitted;
        Json = json;
    }

    /// <summary>
    /// What each variable of type <c>nvarchar(max)</c>, and each such column of a table variable, holds as JSON: see
    /// <see cref="JsonSlot"/>. A place not here is not traced, and reads as its declared type.
    /// </summary>
    public EquatableMap<JsonSlotKey, JsonSlot> Json { get; }

    /// <summary>
    /// Whether the procedure may read uncommitted here: <c>SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED</c> came first on some
    /// path, here or in a caller (observed: A_procedures_isolation_level_holds_in_the_procedures_it_calls_and_ends_when_it_returns).
    /// An isolation level the outermost caller set is not seen: haruspex assumes it does not read uncommitted, because
    /// being strict would remove every key-based claim (the Inference README, "How many rows").
    /// </summary>
    public bool ReadUncommitted { get; }

    /// <summary>The temporary tables the procedure has touched. One it has not is the caller's, if there is one.</summary>
    public EquatableMap<LocalName, TempTable> Temps { get; }

    /// <summary>Each variable's and parameter's current value.</summary>
    public EquatableMap<LocalName, Value> Variables { get; }

    /// <summary>The result sets returned so far, by position.</summary>
    public ResultTrack Results { get; }

    /// <summary>The state a body starts in: its variables and parameters, no temporary tables, no results.</summary>
    public static FlowState Start(IEnumerable<KeyValuePair<LocalName, Value>> variables) =>
        new([], [.. variables], ResultTrack.None, false, []);

    /// <summary>The table's state; null when the procedure has not touched it, which makes it the caller's.</summary>
    public TempTable? Temp(LocalName name) => Temps.GetValueOrDefault(name);

    public FlowState WithTemp(LocalName name, TempTable table) => new(Temps.SetItem(name, table), Variables, Results, ReadUncommitted, Json);

    public FlowState WithTemps(EquatableMap<LocalName, TempTable> temps) => new(temps, Variables, Results, ReadUncommitted, Json);

    public FlowState WithVariable(LocalName name, Value value) => new(Temps, Variables.SetItem(name, value), Results, ReadUncommitted, Json);

    public FlowState WithResults(ResultTrack results) => new(Temps, Variables, results, ReadUncommitted, Json);

    public FlowState WithReadUncommitted(bool readUncommitted) => new(Temps, Variables, Results, readUncommitted, Json);

    public FlowState WithJson(JsonSlotKey place, JsonSlot slot) => new(Temps, Variables, Results, ReadUncommitted, Json.SetItem(place, slot));

    /// <summary>The state after either of two groups of paths; null stands for no path (every one ended before here).</summary>
    public static FlowState? Join(FlowState? a, FlowState? b, SourceLocation at)
    {
        if (a is null || b is null)
            return a ?? b;
        if (ReferenceEquals(a, b))
            return a;

        var temps = a.Temps;
        foreach (var name in a.Temps.Keys.Union(b.Temps.Keys).OrderBy(n => n.Text, StringComparer.Ordinal))
            temps = temps.SetItem(name, JoinTable(name, a.Temp(name), b.Temp(name), at));

        var variables = a.Variables;
        foreach (var (name, value) in b.Variables)
        {
            variables = a.Variables.TryGetValue(name, out var mine)
                ? variables.SetItem(name, Value.Join(mine, value, at, name.Text))
                : variables.SetItem(name, value);
        }

        // A place traced on one path only is not traced on the other (it cannot happen: declarations are the whole body's).
        var json = a.Json;
        foreach (var (place, slot) in b.Json)
            json = a.Json.TryGetValue(place, out var mine) ? json.SetItem(place, JsonSlot.Join(mine, slot)) : json.Remove(place);
        foreach (var place in a.Json.Keys.Where(k => !b.Json.ContainsKey(k)))
            json = json.Remove(place);

        // Uncommitted on either path may be uncommitted here.
        return new FlowState(temps, variables, a.Results.Join(b.Results), a.ReadUncommitted || b.ReadUncommitted, json);
    }

    public static FlowState? Join(IEnumerable<FlowState?> states, SourceLocation at) =>
        states.Aggregate((FlowState?)null, (all, next) => Join(all, next, at));

    /// <summary>
    /// A table on either of two paths. The same table joins column by column; any other pair is unknown, since a read of
    /// it could be of either: a table created on one path only is the caller's on the other, and one dropped on one path
    /// only may be missing.
    /// </summary>
    private static TempTable JoinTable(LocalName name, TempTable? a, TempTable? b, SourceLocation at)
    {
        if (a is null || b is null)
        {
            var known = a ?? b;
            return known is UnknownTable unknown ? unknown : new UnknownTable(new UnknownShape(
                IrUnknownReason.CallerTempTable,
                $"{name} is created on some paths only before {at}; on the others it is the caller's table of that name, if there is one.",
                at));
        }

        return (a, b) switch
        {
            (UnknownTable, _) => a,
            (_, UnknownTable) => b,
            (AbsentTable, AbsentTable) => a,
            (KnownTable x, KnownTable y) when x.Columns.Select(c => c.Name).SequenceEqual(y.Columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase)
                => new KnownTable(x.Name, [.. x.Columns.Zip(y.Columns, (c, d) => TempColumn.Join(c, d, at, $"{name}.{c.Name}"))]),
            (KnownTable, KnownTable) => new UnknownTable(new UnknownShape(IrUnknownReason.UnsupportedStatement, $"{name} has different columns on different paths before {at}.", at)),
            _ => new UnknownTable(new UnknownShape(IrUnknownReason.UnresolvedTable, $"{name} is dropped on some paths before {at}, so it may not exist there.", at)),
        };
    }
}
