using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

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
    private readonly HashSet<LocalRelation> temporary = [];

    /// <summary>Reads a derived table's query; null leaves derived tables unmodelled.</summary>
    public Func<QueryDerivedTable, BindingScope?, LocalRelationValues>? ReadDerived { get; init; }

    public LocalRelation? Derived(QueryDerivedTable table, BindingScope? outer) =>
        ReadDerived is null ? null : Register(table.Alias?.Value ?? "", ReadDerived(table, outer));

    /// <summary>Reads a VALUES list's rows; null leaves VALUES lists unmodelled.</summary>
    public Func<InlineDerivedTable, BindingScope?, LocalRelationValues>? ReadValues { get; init; }

    public LocalRelation? Values(InlineDerivedTable table, BindingScope? outer) =>
        ReadValues is null ? null : Register(table.Alias?.Value ?? "", ReadValues(table, outer));

    /// <summary>Reads a view's definition; null leaves views unmodelled.</summary>
    public Func<NamedObject, LocalRelationValues>? ReadView { get; init; }

    public LocalRelation? View(NamedObject view) =>
        ReadView is null ? null : Register(view.Name.ToString(), ReadView(view));

    /// <summary>Reads a call of a table-valued function; null leaves functions in FROM unmodelled.</summary>
    public Func<SchemaObjectFunctionTableReference, BindingScope?, LocalRelationValues>? ReadFunction { get; init; }

    public LocalRelation? TableFunction(SchemaObjectFunctionTableReference reference, BindingScope? outer) =>
        ReadFunction is null ? null : Register(reference.Alias?.Value ?? reference.SchemaObject.BaseIdentifier.Value, ReadFunction(reference, outer));

    /// <summary>Whether a relation handed out is a temporary table (#t), rather than a CTE, derived table or table variable.</summary>
    public bool IsTemporary(LocalRelation relation) => temporary.Contains(relation);
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

        var found = State.Temp(name) switch
        {
            KnownTable known => Register(name, new LocalRelationValues([.. known.Columns.Select(c => c.Name)], [.. known.Columns.Select(c => c.Value)], null)),
            UnknownTable unknown => Register(name, LocalRelationValues.Unknown(unknown.Why)),
            AbsentTable absent => Register(name, LocalRelationValues.Unknown(new UnknownShape(IrUnknownReasons.UnresolvedTable, $"{name} was dropped at {absent.At}, so it is not there to read.", absent.At))),
            _ => Register(name, LocalRelationValues.Unknown(new UnknownShape(
                IrUnknownReasons.CallerTempTable,
                $"{name} is not created by the procedure before this, so it is the caller's, whose shape depends on who calls.",
                Nowhere))),
        };
        temporary.Add(found);
        return found;
    }

    /// <summary>A placeholder location for an unknown that has none of its own; each read reports its own location instead.</summary>
    private static readonly SourceLocation Nowhere = new("", 0, 0);

    public LocalRelation Register(string name, LocalRelationValues values)
    {
        var relation = new LocalRelation(name, values.Names);
        registry[relation] = values;
        return relation;
    }

    /// <summary>What every column of a relation handed out holds, or why it is not known.</summary>
    public LocalRelationValues Values(LocalRelation relation) => registry[relation];

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

    /// <summary>
    /// How many rows it has where it is read (for an APPLY's right side, for each outer row): set for derived tables,
    /// VALUES lists and views from their query (Cardinality.cs); any number for the others, which claims nothing.
    /// </summary>
    public IrCardinality Rows { get; init; } = IrCardinality.Many;

    /// <summary>
    /// Whether it always has a row: a query of exactly one row, or a VALUES list. On an OUTER APPLY's right side it is never
    /// null-extended (observed: An_ungrouped_aggregate_applied_gives_one_row_for_every_outer_row).
    /// </summary>
    public bool AtLeastOne { get; init; }

    /// <summary>Its unique keys, which only a view has here (see <see cref="ProcedureAnalyzer"/>'s <c>ViewKeys</c>).</summary>
    public IReadOnlyList<RelationKey> Keys { get; init; } = [];
}

/// <summary>A relation's unique key: columns whose values no two of its rows share, at most one row holding NULL in all.</summary>
/// <param name="Ordinals">The key's columns, by position in the relation.</param>
/// <param name="Types">Each column's built-in SQL type, for comparing it with a value in its own type (Cardinality.cs).</param>
internal sealed record RelationKey(IReadOnlyList<int> Ordinals, IReadOnlyList<string> Types);
