using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The relations and variables a procedure's body defines, as they stand at the statement being read: the CTEs of that
/// statement, the temporary tables of the current <see cref="FlowState"/>, the table variables and the variables. The
/// binder finds relations here by name (<see cref="ILocalRelations"/>); what their columns hold stays here, keyed by the
/// relation it handed out.
/// </summary>
internal sealed class FlowLocals(IReadOnlyDictionary<string, LocalRelationValues> tableVariables) : ILocalRelations
{
    private readonly Dictionary<LocalRelation, LocalRelationValues> registry = [];
    private readonly List<Dictionary<string, LocalRelation>> cteFrames = [];

    /// <summary>The state the statement being read starts from.</summary>
    public FlowState State { get; set; } = FlowState.Start([]);

    public LocalRelation? Find(string name)
    {
        // The innermost WITH first: a CTE hides one of the same name outside it, and any table.
        for (var i = cteFrames.Count - 1; i >= 0; i--)
        {
            if (cteFrames[i].TryGetValue(name, out var cte))
                return cte;
        }

        if (name.StartsWith('@'))
            return tableVariables.TryGetValue(name, out var variable) ? Register(name, variable) : null;
        if (!name.StartsWith('#'))
            return null;

        // A global temporary table (##t) is shared with every session, which may change it at any time.
        if (name.StartsWith("##", StringComparison.Ordinal))
            return Register(name, LocalRelationValues.Unknown(new UnknownShape(IrUnknownReasons.UnsupportedSource, $"{name} is a global temporary table, which any session may change.", Nowhere)));

        return State.Temp(name) switch
        {
            KnownTable known => Register(name, new LocalRelationValues([.. known.Columns.Select(c => c.Name)], [.. known.Columns.Select(c => c.Value)], null)),
            UnknownTable unknown => Register(name, LocalRelationValues.Unknown(unknown.Why)),
            AbsentTable absent => Register(name, LocalRelationValues.Unknown(new UnknownShape(IrUnknownReasons.UnresolvedTable, $"{name} was dropped at {absent.At}, so it is not there to read.", absent.At))),
            _ => Register(name, LocalRelationValues.Unknown(new UnknownShape(
                IrUnknownReasons.CallerTempTable,
                $"{name} is not created by the procedure before this, so it is the caller's, whose shape depends on who calls.",
                Nowhere))),
        };
    }

    /// <summary>A placeholder location for an unknown that has none of its own; each read reports its own location instead.</summary>
    private static readonly SourceLocation Nowhere = new("", 0, 0);

    public LocalRelation Register(string name, LocalRelationValues values)
    {
        var relation = new LocalRelation(name, values.Names);
        registry[relation] = values;
        return relation;
    }

    /// <summary>What a column of a relation handed out holds, read at <paramref name="at"/>.</summary>
    public Value Column(LocalRelation relation, int ordinal, SourceLocation at)
    {
        var values = registry[relation];
        if (values.Why is { } why)
        {
            // Reported where it is read, as a binding problem is; the detail names the statement that caused it.
            return Value.Unknown(why with { At = at });
        }

        return values.Values![ordinal];
    }

    /// <summary>Makes the CTEs of one WITH visible until <see cref="PopCtes"/>; they are added to it one by one.</summary>
    public Dictionary<string, LocalRelation> PushCtes()
    {
        var frame = new Dictionary<string, LocalRelation>(StringComparer.OrdinalIgnoreCase);
        cteFrames.Add(frame);
        return frame;
    }

    public void PopCtes() => cteFrames.RemoveAt(cteFrames.Count - 1);
}

/// <summary>The columns of a relation and what each holds, or why they are not known.</summary>
/// <param name="Names">The columns' names; null when not known.</param>
/// <param name="Values">What each holds, by position; null when not known.</param>
/// <param name="Why">Why they are not known.</param>
internal sealed record LocalRelationValues(IReadOnlyList<string>? Names, IReadOnlyList<Value>? Values, UnknownShape? Why)
{
    public static LocalRelationValues Unknown(UnknownShape why) => new(null, null, why);
}
