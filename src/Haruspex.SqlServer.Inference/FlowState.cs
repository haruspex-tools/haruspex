using System.Collections.Immutable;
using System.Text;
using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The state of a procedure at a point on its paths: each temporary table, each variable's value, and the result sets
/// returned so far. Immutable, so a branch can start from the state before it and both branches' states can be joined.
/// </summary>
internal sealed class FlowState
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    private FlowState(ImmutableDictionary<string, TempTable> temps, ImmutableDictionary<string, Value> variables, ResultTrack results, bool readUncommitted = false, ImmutableDictionary<string, JsonSlot>? json = null)
    {
        Temps = temps;
        Variables = variables;
        Results = results;
        ReadUncommitted = readUncommitted;
        Json = json ?? ImmutableDictionary.Create<string, JsonSlot>(Names);
    }

    /// <summary>
    /// What each variable of type <c>nvarchar(max)</c>, and each such column of a table variable (<c>@t.c</c>), holds as JSON
    /// (slice 6l): see <see cref="JsonSlot"/>. A name not here is not traced, and reads as its declared type.
    /// </summary>
    public ImmutableDictionary<string, JsonSlot> Json { get; }

    /// <summary>
    /// Whether the procedure may read uncommitted here: <c>SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED</c> came first on some
    /// path, here or in a caller (observed: A_procedures_isolation_level_holds_in_the_procedures_it_calls_and_ends_when_it_returns).
    /// An isolation level the outermost caller set is not seen: haruspex assumes it does not read uncommitted
    /// (haruspex-design.md, "What can and cannot be typed").
    /// </summary>
    public bool ReadUncommitted { get; }

    /// <summary>The temporary tables the procedure has touched. One it has not is the caller's, if there is one.</summary>
    public ImmutableDictionary<string, TempTable> Temps { get; }

    /// <summary>Each variable's and parameter's current value.</summary>
    public ImmutableDictionary<string, Value> Variables { get; }

    public ResultTrack Results { get; }

    public static FlowState Start(IEnumerable<KeyValuePair<string, Value>> variables) =>
        new(ImmutableDictionary.Create<string, TempTable>(Names), ImmutableDictionary.CreateRange(Names, variables), ResultTrack.None);

    /// <summary>The table's state; null when the procedure has not touched it, which makes it the caller's.</summary>
    public TempTable? Temp(string name) => Temps.GetValueOrDefault(name);

    public FlowState WithTemp(string name, TempTable table) => new(Temps.SetItem(name, table), Variables, Results, ReadUncommitted, Json);

    public FlowState WithTemps(ImmutableDictionary<string, TempTable> temps) => new(temps, Variables, Results, ReadUncommitted, Json);

    public FlowState WithVariable(string name, Value value) => new(Temps, Variables.SetItem(name, value), Results, ReadUncommitted, Json);

    public FlowState WithResults(ResultTrack results) => new(Temps, Variables, results, ReadUncommitted, Json);

    public FlowState WithReadUncommitted(bool readUncommitted) => new(Temps, Variables, Results, readUncommitted, Json);

    public FlowState WithJson(string name, JsonSlot slot) => new(Temps, Variables, Results, ReadUncommitted, Json.SetItem(name, slot));

    /// <summary>The state after either of two groups of paths; null stands for no path (every one ended before here).</summary>
    public static FlowState? Join(FlowState? a, FlowState? b, SourceLocation at)
    {
        if (a is null || b is null)
            return a ?? b;
        if (ReferenceEquals(a, b))
            return a;

        var temps = a.Temps;
        foreach (var name in a.Temps.Keys.Union(b.Temps.Keys, Names).Order(StringComparer.Ordinal))
            temps = temps.SetItem(name, JoinTable(name, a.Temp(name), b.Temp(name), at));

        var variables = a.Variables;
        foreach (var (name, value) in b.Variables)
        {
            variables = a.Variables.TryGetValue(name, out var mine)
                ? variables.SetItem(name, ShapeJoin.Either(mine, value, at, name))
                : variables.SetItem(name, value);
        }

        // A name traced on one path only is not traced on the other (it cannot happen: declarations are the whole body's).
        var json = a.Json;
        foreach (var (name, slot) in b.Json)
            json = a.Json.TryGetValue(name, out var mine) ? json.SetItem(name, JsonSlot.Join(mine, slot)) : json.Remove(name);
        foreach (var name in a.Json.Keys.Where(k => !b.Json.ContainsKey(k)))
            json = json.Remove(name);

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
    private static TempTable JoinTable(string name, TempTable? a, TempTable? b, SourceLocation at)
    {
        if (a is null || b is null)
        {
            var known = a ?? b;
            return known is UnknownTable unknown ? unknown : new UnknownTable(new UnknownShape(
                IrUnknownReasons.CallerTempTable,
                $"{name} is created on some paths only before {at}; on the others it is the caller's table of that name, if there is one.",
                at));
        }

        return (a, b) switch
        {
            (UnknownTable, _) => a,
            (_, UnknownTable) => b,
            (AbsentTable, AbsentTable) => a,
            (KnownTable x, KnownTable y) when x.Columns.Select(c => c.Name).SequenceEqual(y.Columns.Select(c => c.Name), Names)
                => new KnownTable(x.Name, [.. x.Columns.Zip(y.Columns, (c, d) => c with
                {
                    Value = ShapeJoin.Either(c.Value, d.Value, at, $"{name}.{c.Name}"),
                    // A column declared alike on both paths keeps its declaration. One declared on some paths only, or
                    // differently, is not guaranteed by either declaration, so it joins later writes like a column SELECT …
                    // INTO made, and is never read again from one path's declaration.
                    Declaration = ReferenceEquals(c.Declaration, d.Declaration) ? c.Declaration : null,
                    Identity = c.Identity == d.Identity ? c.Identity : null,
                    Json = c.Json is { } x && d.Json is { } y ? JsonSlot.Join(x, y) : null,
                })]),
            (KnownTable, KnownTable) => new UnknownTable(new UnknownShape(IrUnknownReasons.UnsupportedStatement, $"{name} has different columns on different paths before {at}.", at)),
            _ => new UnknownTable(new UnknownShape(IrUnknownReasons.UnresolvedTable, $"{name} is dropped on some paths before {at}, so it may not exist there.", at)),
        };
    }

    /// <summary>The state as text, for a loop to tell when another pass changes nothing.</summary>
    public string Text()
    {
        var text = new StringBuilder(TempsText());
        text.Append(ReadUncommitted ? "uncommitted\n" : "");
        foreach (var (name, value) in Variables.OrderBy(v => v.Key, StringComparer.Ordinal))
            text.Append(name).Append('=').Append(ShapeText.Of(value)).Append('\n');
        foreach (var (name, slot) in Json.OrderBy(v => v.Key, StringComparer.Ordinal))
            text.Append(name).Append(':').Append(slot.Text()).Append('\n');
        Results.Write(text);
        return text.ToString();
    }

    /// <summary>The temporary tables as text: what a callee starts from, so it keys a call's walk in a cache.</summary>
    public string TempsText()
    {
        var text = new StringBuilder();
        foreach (var (name, table) in Temps.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            text.Append(name).Append('=');
            switch (table)
            {
                case AbsentTable absent:
                    text.Append("absent@").Append(absent.At);
                    break;
                case UnknownTable unknown:
                    text.Append("unknown:").Append(unknown.Why.Reason).Append('@').Append(unknown.Why.At);
                    break;
                case KnownTable known:
                    foreach (var column in known.Columns)
                    {
                        text.Append(column.Name).Append(column.Identity switch { true => "+", false => "", null => "?" })
                            .Append(column.Declaration is null ? ':' : '!').Append(ShapeText.Of(column.Value)).Append(column.Json?.Text()).Append(';');
                    }

                    break;
            }

            text.Append('\n');
        }

        return text.ToString();
    }
}
