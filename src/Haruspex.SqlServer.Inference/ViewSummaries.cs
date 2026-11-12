using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// What each view of the project holds, read once per document from its definition (<see cref="ProcedureAnalyzer.ReadView"/>),
/// and what each call of a table-valued function returns, read once per function and set of argument values. A
/// view or function reads others; one that reads itself through them cannot be created, and is unknown here.
/// The idea, and the alternatives, are in docs/internals/07-summaries-and-recursion.md.
/// </summary>
internal sealed class ViewSummaries
{
    private readonly DatabaseModel model;
    private readonly FunctionSummaries functions;
    private readonly Dictionary<(ObjectName, bool), LocalRelationValues> done = [];
    private readonly HashSet<ObjectName> reading = [];
    private readonly Dictionary<Reading, LocalRelationValues> calls = [];

    public ViewSummaries(DatabaseModel model, FunctionSummaries functions)
    {
        this.model = model;
        this.functions = functions;

        // A scalar function's body may read views too.
        functions.Views = this;
    }

    /// <summary>
    /// What a call of a table-valued function returns, with its parameters holding <paramref name="arguments"/>: an inline
    /// function's query read with them (<see cref="ProcedureAnalyzer.ReadInline"/>), a multi-statement one's declared return
    /// table (<see cref="ProcedureAnalyzer.ReadReturnTable"/>).
    /// </summary>
    /// <param name="uncommitted">Whether it is read uncommitted (Cardinality.cs), so that no key of what it reads holds.</param>
    public LocalRelationValues Function(UserFunction function, IReadOnlyDictionary<string, Value> arguments, bool uncommitted)
    {
        var key = new Reading(function.Name, uncommitted, [.. arguments.OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase)]);
        if (calls.TryGetValue(key, out var found))
            return found;
        if (!reading.Add(function.Name))
            return LocalRelationValues.Unknown(new UnknownShape(IrUnknownReason.UnresolvedTable, $"{function.Name} reads itself through the functions and views it reads.", function.At));

        try
        {
            var analyser = new ProcedureAnalyzer(model, function.File, Functions(uncommitted), arguments, ansiNullsOff: function.AnsiNullsOff, views: this, uncommittedReads: uncommitted);
            found = function.Body.ReturnType switch
            {
                SelectFunctionReturnType inline => analyser.ReadInline(function, inline.SelectStatement),
                TableValuedFunctionReturnType declared => analyser.ReadReturnTable(function, declared),
                _ => throw new ArgumentException("Not a table-valued function.", nameof(function)),
            };
        }
        finally
        {
            reading.Remove(function.Name);
        }

        calls[key] = found;
        return found;
    }

    /// <summary>A placeholder location for an unknown about the view as a whole; each read reports its own location instead.</summary>
    private static readonly Diagnostics.SourceLocation Nowhere = new("", 0, 0);

    /// <summary>The scalar functions' summaries for a body read committed or uncommitted (<see cref="FunctionSummaries.Uncommitted"/>).</summary>
    private FunctionSummaries Functions(bool uncommitted) => uncommitted ? functions.Uncommitted : functions;

    /// <summary>A view's rows, read once per document and per way of reading it: committed, or uncommitted inside too.</summary>
    /// <param name="uncommitted">
    /// Whether it is read uncommitted: under a NOLOCK or READUNCOMMITTED hint on it, which applies to every table under it, or
    /// in a statement that reads uncommitted. Then no key of the tables inside holds.
    /// </param>
    public LocalRelationValues Read(NamedObject view, bool uncommitted)
    {
        if (done.TryGetValue((view.Name, uncommitted), out var found))
            return found;

        // A view created and altered, or defined twice, depends on the order the scripts run (Sources).
        if (view.View is null || view.File is null)
            return LocalRelationValues.Unknown(new UnknownShape(IrUnknownReason.UnresolvedTable, $"{view.Name} is defined more than once, and which definition applies depends on the order the scripts run.", Nowhere));
        if (!reading.Add(view.Name))
            return LocalRelationValues.Unknown(new UnknownShape(IrUnknownReason.UnresolvedTable, $"{view.Name} reads itself through the views it reads, which SQL Server refuses.", Nowhere));

        try
        {
            found = new ProcedureAnalyzer(model, view.File, Functions(uncommitted), ansiNullsOff: view.AnsiNullsOff, views: this, uncommittedReads: uncommitted).ReadView(view);
        }
        finally
        {
            reading.Remove(view.Name);
        }

        done[(view.Name, uncommitted)] = found;
        return found;
    }

    /// <summary>
    /// One reading of a table-valued function: the function, whether it is read uncommitted, and each argument's value by
    /// parameter name, in name order. Two calls with equal arguments, compared by value, read the same rows.
    /// </summary>
    private sealed record Reading(ObjectName Function, bool Uncommitted, EquatableArray<KeyValuePair<string, Value>> Arguments);
}
