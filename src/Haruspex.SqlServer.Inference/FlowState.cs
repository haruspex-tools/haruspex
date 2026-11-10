using System.Collections.Immutable;
using System.Text;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Inference;

// What the statement walk (ProcedureAnalyzer.Flow.cs) carries from one statement to the next, and how two states join
// where paths meet. README.md in this folder gives the model and why each join is sound.

/// <summary>What is known of one temporary table at a point in the procedure.</summary>
internal abstract record TempTable;

/// <summary>
/// The table is not there: it was dropped at <paramref name="At"/>. A read of it fails the call (error 208, observed not
/// to be caught by a CATCH in the same procedure), so it never gives a value; a join with any other state is unknown.
/// </summary>
internal sealed record AbsentTable(SourceLocation At) : TempTable;

/// <summary>Nothing is known of the table's shape, for the reason given, which every column read from it reports.</summary>
internal sealed record UnknownTable(UnknownShape Why) : TempTable;

/// <summary>The procedure created the table, so its columns and what each may hold are known.</summary>
/// <param name="Name">The table's name, <c>#t</c>.</param>
/// <param name="Columns">Its columns, in order.</param>
internal sealed record KnownTable(string Name, IReadOnlyList<TempColumn> Columns) : TempTable;

/// <summary>A column of a <see cref="KnownTable"/>.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Value">What it may hold: the type of every value, and why one may be null.</param>
/// <param name="Declaration">
/// The table definition that declared it (<c>CREATE TABLE</c>, <c>ALTER TABLE … ADD</c>), with its constraints, when it was
/// declared; null for a column <c>SELECT … INTO</c> made. A declared column's value comes from its declaration alone, so a
/// write never changes it: <c>NOT NULL</c> is enforced, and a write converts to the declared type. A column made by
/// <c>SELECT … INTO</c> has a type, but its nullability is SQL Server's own reading of the select, which haruspex does not
/// reproduce (observed), so each write joins with what it held.
/// </param>
internal sealed record TempColumn(string Name, Value Value, Table? Declaration);

/// <summary>A result set one path returns at a position, from the statement at <paramref name="At"/>.</summary>
internal sealed record Produced(ResultShape Result, SourceLocation At, string Key);

/// <summary>
/// The result sets the paths reaching a point have returned. Result sets are positional: a client receives them in order,
/// and one that does not run is not there at all, so a later one takes its place (observed). So the track keeps, for each
/// position, every result some path returned there, and the numbers of results the paths have returned so far. A result
/// returned now lands at every position some path is at.
/// </summary>
internal sealed class ResultTrack
{
    private ResultTrack(ImmutableList<ImmutableList<Produced>> positions, ImmutableSortedSet<int> counts)
    {
        Positions = positions;
        Counts = counts;
    }

    public static ResultTrack None { get; } = new([], [0]);

    /// <summary>For each position, what the paths returned there.</summary>
    public ImmutableList<ImmutableList<Produced>> Positions { get; }

    /// <summary>How many result sets the paths have returned: one number per group of paths.</summary>
    public ImmutableSortedSet<int> Counts { get; }

    public ResultTrack Append(ResultShape result, SourceLocation at)
    {
        var produced = new Produced(result, at, Key(result, at));
        var positions = Positions;
        foreach (var count in Counts)
        {
            while (positions.Count <= count)
                positions = positions.Add([]);
            if (!positions[count].Any(p => p.Key == produced.Key))
                positions = positions.SetItem(count, positions[count].Add(produced));
        }

        return new ResultTrack(positions, [.. Counts.Select(c => c + 1)]);
    }

    public ResultTrack Join(ResultTrack other)
    {
        var positions = Positions;
        for (var i = 0; i < other.Positions.Count; i++)
        {
            if (positions.Count <= i)
                positions = positions.Add([]);
            foreach (var produced in other.Positions[i])
            {
                if (!positions[i].Any(p => p.Key == produced.Key))
                    positions = positions.SetItem(i, positions[i].Add(produced));
            }
        }

        return new ResultTrack(positions, Counts.Union(other.Counts));
    }

    public void Write(StringBuilder text)
    {
        foreach (var position in Positions)
            text.Append('[').AppendJoin(';', position.Select(p => p.Key)).Append(']');
        text.Append('#').AppendJoin(',', Counts);
    }

    private static string Key(ResultShape result, SourceLocation at)
    {
        var text = new StringBuilder();
        text.Append(at).Append('|').Append(result.Encoding).Append('|').Append(ShapeText.Of(result.Shape)).Append('|');
        foreach (var reason in result.AbsentBecause)
            text.Append(reason.Rule).Append('@').Append(reason.At).Append(',');
        return text.ToString();
    }
}

/// <summary>
/// The state of a procedure at a point on its paths: each temporary table, each variable's value, and the result sets
/// returned so far. Immutable, so a branch can start from the state before it and both branches' states can be joined.
/// </summary>
internal sealed class FlowState
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    private FlowState(ImmutableDictionary<string, TempTable> temps, ImmutableDictionary<string, Value> variables, ResultTrack results)
    {
        Temps = temps;
        Variables = variables;
        Results = results;
    }

    /// <summary>The temporary tables the procedure has touched. One it has not is the caller's, if there is one.</summary>
    public ImmutableDictionary<string, TempTable> Temps { get; }

    /// <summary>Each variable's and parameter's current value.</summary>
    public ImmutableDictionary<string, Value> Variables { get; }

    public ResultTrack Results { get; }

    public static FlowState Start(IEnumerable<KeyValuePair<string, Value>> variables) =>
        new(ImmutableDictionary.Create<string, TempTable>(Names), ImmutableDictionary.CreateRange(Names, variables), ResultTrack.None);

    /// <summary>The table's state; null when the procedure has not touched it, which makes it the caller's.</summary>
    public TempTable? Temp(string name) => Temps.GetValueOrDefault(name);

    public FlowState WithTemp(string name, TempTable table) => new(Temps.SetItem(name, table), Variables, Results);

    public FlowState WithTemps(ImmutableDictionary<string, TempTable> temps) => new(temps, Variables, Results);

    public FlowState WithVariable(string name, Value value) => new(Temps, Variables.SetItem(name, value), Results);

    public FlowState WithResults(ResultTrack results) => new(Temps, Variables, results);

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

        return new FlowState(temps, variables, a.Results.Join(b.Results));
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

        switch (a, b)
        {
            case (UnknownTable, _):
                return a;
            case (_, UnknownTable):
                return b;
            case (AbsentTable, AbsentTable):
                return a;
            case (KnownTable x, KnownTable y) when x.Columns.Select(c => c.Name).SequenceEqual(y.Columns.Select(c => c.Name), Names):
                return new KnownTable(x.Name, [.. x.Columns.Zip(y.Columns, (c, d) => c with
                {
                    Value = ShapeJoin.Either(c.Value, d.Value, at, $"{name}.{c.Name}"),
                    Declaration = ReferenceEquals(c.Declaration, d.Declaration) ? c.Declaration : c.Declaration ?? d.Declaration,
                })]);
            case (KnownTable, KnownTable):
                return new UnknownTable(new UnknownShape(IrUnknownReasons.UnsupportedStatement, $"{name} has different columns on different paths before {at}.", at));
            default:
                return new UnknownTable(new UnknownShape(IrUnknownReasons.UnresolvedTable, $"{name} is dropped on some paths before {at}, so it may not exist there.", at));
        }
    }

    /// <summary>The state as text, for a loop to tell when another pass changes nothing.</summary>
    public string Text()
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
                        text.Append(column.Name).Append(column.Declaration is null ? ':' : '!').Append(ShapeText.Of(column.Value)).Append(';');
                    break;
            }

            text.Append('\n');
        }

        foreach (var (name, value) in Variables.OrderBy(v => v.Key, StringComparer.Ordinal))
            text.Append(name).Append('=').Append(ShapeText.Of(value)).Append('\n');
        Results.Write(text);
        return text.ToString();
    }
}
