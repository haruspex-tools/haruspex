using System.Diagnostics.CodeAnalysis;
using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The state of a procedure at a point on its paths: each temporary table, each variable's value, the result sets
/// returned so far, and what a guard could prove of the variables a SELECT may have assigned. Immutable, so a branch can start from the state before it and both branches' states can be joined.
/// </summary>
internal sealed record FlowState
{
    private FlowState(
        EquatableMap<LocalName, TempTable> temps,
        EquatableMap<LocalName, Value> variables,
        ResultTrack results,
        bool readUncommitted,
        EquatableMap<JsonSlotKey, JsonSlot> json,
        EquatableArray<LocalName> unassigned,
        EquatableArray<PossibleAssignment> possible,
        SessionFacts session)
    {
        Temps = temps;
        Variables = variables;
        Results = results;
        ReadUncommitted = readUncommitted;
        Json = json;
        Unassigned = unassigned;
        Possible = possible;
        Transactions = session.Transactions;
        Isolation = session.Isolation;
        Found = session.Found;
    }

    /// <summary>
    /// What the body's own transactions and isolation level are here, and the rows its existence guards found: the facts an
    /// existence guard's claim rests on (<see cref="FoundRow"/>). Kept as one part, since they change together.
    /// </summary>
    private sealed record SessionFacts(int Transactions, IsolationLevel? Isolation, EquatableArray<FoundRow> Found)
    {
        public static SessionFacts None { get; } = new(0, null, []);
    }

    private SessionFacts Session => new(Transactions, Isolation, Found);

    /// <summary>
    /// How many transactions the body has begun and not yet ended on every path here (<c>@@TRANCOUNT</c> less the caller's,
    /// which haruspex doesn't see): the fewest over the paths, since a hold lasts only while its transaction is open on each.
    /// </summary>
    public int Transactions { get; }

    /// <summary>The isolation level the body set and every path here agrees on; null when it is the caller's, which haruspex doesn't see.</summary>
    public IsolationLevel? Isolation { get; }

    /// <summary>The rows existence guards found here (<see cref="FoundRow"/>), kept where paths meet only if every path holds them.</summary>
    public EquatableArray<FoundRow> Found { get; }

    /// <summary>
    /// The variables the body declares that no path to here has assigned, so each certainly holds NULL: a must-fact, kept
    /// where paths meet only when it holds on each. A parameter is the caller's, so never here.
    /// </summary>
    public EquatableArray<LocalName> Unassigned { get; }

    /// <summary>What each SELECT that may have read no row assigned if it read one (<see cref="PossibleAssignment"/>).</summary>
    public EquatableArray<PossibleAssignment> Possible { get; }

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
    /// <param name="variables">Each variable's and parameter's value at the start.</param>
    /// <param name="declared">The variables the body declares, each NULL until assigned.</param>
    public static FlowState Start(IEnumerable<KeyValuePair<LocalName, Value>> variables, IEnumerable<LocalName>? declared = null) =>
        new([], [.. variables], ResultTrack.None, false, [], [.. declared ?? []], [], SessionFacts.None);

    /// <summary>The table's state; null when the procedure has not touched it, which makes it the caller's.</summary>
    public TempTable? Temp(LocalName name) => Temps.GetValueOrDefault(name);

    public FlowState WithTemp(LocalName name, TempTable table) => new(Temps.SetItem(name, table), Variables, Results, ReadUncommitted, Json, Unassigned, Possible, Session);

    public FlowState WithTemps(EquatableMap<LocalName, TempTable> temps) => new(temps, Variables, Results, ReadUncommitted, Json, Unassigned, Possible, Session);

    public FlowState WithVariable(LocalName name, Value value) => new(Temps, Variables.SetItem(name, value), Results, ReadUncommitted, Json, Unassigned, Possible, Session);

    public FlowState WithResults(ResultTrack results) => new(Temps, Variables, results, ReadUncommitted, Json, Unassigned, Possible, Session);

    public FlowState WithReadUncommitted(bool readUncommitted) => new(Temps, Variables, Results, readUncommitted, Json, Unassigned, Possible, Session);

    public FlowState WithJson(JsonSlotKey place, JsonSlot slot) => new(Temps, Variables, Results, ReadUncommitted, Json.SetItem(place, slot), Unassigned, Possible, Session);

    /// <summary>
    /// This state after <paramref name="name"/> is assigned: no longer certainly NULL, and every possible assignment that
    /// names it ended, since what it holds is no longer the SELECT's.
    /// </summary>
    public FlowState Assigned(LocalName name) =>
        new(Temps, Variables, Results, ReadUncommitted, Json, [.. Unassigned.Where(n => n != name)], [.. Possible.Where(p => !p.Values.ContainsKey(name))], Session with { Found = [.. Found.Where(f => !f.Reads(name))] });

    /// <summary>This state with what a SELECT that may have read no row assigned if it read one.</summary>
    public FlowState WithPossible(PossibleAssignment possible) =>
        new(Temps, Variables, Results, ReadUncommitted, Json, Unassigned, [.. Possible, possible], Session);

    /// <summary>This state with the isolation level a <c>SET TRANSACTION ISOLATION LEVEL</c> in the body sets.</summary>
    public FlowState WithIsolation(IsolationLevel isolation) =>
        new(Temps, Variables, Results, ReadUncommitted, Json, Unassigned, Possible, Session with { Isolation = isolation });

    /// <summary>This state after <c>BEGIN TRANSACTION</c>.</summary>
    public FlowState Begun() =>
        new(Temps, Variables, Results, ReadUncommitted, Json, Unassigned, Possible, Session with { Transactions = Transactions + 1 });

    /// <summary>
    /// This state after <c>COMMIT</c>: one transaction fewer, and when none of the body's is left, no row held any more. A
    /// COMMIT of a transaction the caller began is not counted, which only claims less.
    /// </summary>
    public FlowState Committed()
    {
        var depth = Math.Max(0, Transactions - 1);
        return new(Temps, Variables, Results, ReadUncommitted, Json, Unassigned, Possible, new SessionFacts(
            depth, Isolation, depth == 0 ? [.. Found.Select(f => f.Raced)] : Found));
    }

    /// <summary>
    /// This state after something that may change a table's rows: a write, a call, a rollback. No row a guard found is known
    /// to be there any more, raced or not. A rollback also ends every transaction the body began.
    /// </summary>
    public FlowState Written(bool rolledBack = false) => Found.Count == 0 && !rolledBack
        ? this
        : new(Temps, Variables, Results, ReadUncommitted, Json, Unassigned, Possible, new SessionFacts(rolledBack ? 0 : Transactions, Isolation, []));

    /// <summary>
    /// The state a CATCH starts from: the error that reached it may have rolled the transaction back (a deadlock victim's does,
    /// 1205, as Microsoft documents), releasing every lock, so no row is held any more and none of the body's transactions is
    /// known to be open. That only claims less where the transaction survived.
    /// </summary>
    public FlowState Caught() => Found.Count == 0 && Transactions == 0
        ? this
        : new(Temps, Variables, Results, ReadUncommitted, Json, Unassigned, Possible, new SessionFacts(0, Isolation, [.. Found.Select(f => f.Raced)]));

    /// <summary>This state with a row an existence guard found.</summary>
    public FlowState WithFound(FoundRow found) =>
        new(Temps, Variables, Results, ReadUncommitted, Json, Unassigned, Possible, Session with { Found = [.. Found, found] });

    /// <summary>This state after a statement that resets <c>@@ROWCOUNT</c>: no possible assignment is counted by it any more.</summary>
    public FlowState Uncounted() => Possible.Any(p => p.Counted)
        ? new(Temps, Variables, Results, ReadUncommitted, Json, Unassigned, [.. Possible.Select(p => p with { Counted = false })], Session)
        : this;

    /// <summary>The state after either of two groups of paths; null stands for no path (every one ended before here).</summary>
    [return: NotNullIfNotNull(nameof(a))]
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

        // Uncommitted on either path may be uncommitted here. A variable is certainly NULL here only if it is on both paths, and
        // a SELECT's possible assignment and a guard's found row hold only if both paths hold them. A transaction is open only
        // as deep as the shallower path's, and an isolation level is known only where both paths set the same.
        return new FlowState(
            temps,
            variables,
            a.Results.Join(b.Results),
            a.ReadUncommitted || b.ReadUncommitted,
            json,
            [.. a.Unassigned.Where(b.Unassigned.Contains)],
            [.. a.Possible.Where(b.Possible.Contains)],
            new SessionFacts(Math.Min(a.Transactions, b.Transactions), a.Isolation == b.Isolation ? a.Isolation : null, FoundRows(a.Found, b.Found)));
    }

    /// <summary>
    /// The rows both paths found: one held on both stays held, and one held on one path only is raced (<see cref="FoundRow.Raced"/>),
    /// as after a COMMIT on one branch.
    /// </summary>
    private static EquatableArray<FoundRow> FoundRows(EquatableArray<FoundRow> a, EquatableArray<FoundRow> b) =>
        [.. a.Select(f => b.Contains(f) ? f : b.Select(g => g.Raced).Contains(f.Raced) ? f.Raced : null).OfType<FoundRow>().Distinct()];

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
