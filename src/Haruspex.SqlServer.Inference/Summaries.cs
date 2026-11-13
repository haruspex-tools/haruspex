using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// What every body of a document may ask about the objects it reads or calls, worked out once per document: what each scalar
/// function returns (<see cref="FunctionSummaries"/>), what each view and each call of a table-valued function holds
/// (<see cref="ViewSummaries"/>), what each call of a procedure does (<see cref="ProcedureCalls"/>), and what the triggers a
/// write fires may return (<see cref="DmlTriggers"/>); with the bindings every body shares. The guide's chapter
/// <c>docs/internals/07-summaries-and-recursion.md</c> explains summaries.
/// </summary>
/// <remarks>
/// The one root of them, built by <see cref="IrInference"/>. A function's body may read a view and a view's may call a
/// function, so each summary reads a body through this root, which it is handed with each question rather than holding: no
/// summary knows another, and nothing is wired after it is built.
/// </remarks>
/// <param name="model">The model the document is inferred from.</param>
/// <param name="declarations">The results declared for procedures whose results can't be inferred.</param>
/// <param name="warnings">Where this run of inference adds what could not be checked, once each (<see cref="DmlTriggers"/>); the run returns them.</param>
internal sealed class Summaries(DatabaseModel model, IReadOnlyDictionary<ObjectName, DeclaredResults> declarations, List<Diagnostic> warnings)
{
    private readonly FunctionSummaries functions = new(model, uncommitted: false);
    private readonly FunctionSummaries uncommittedFunctions = new(model, uncommitted: true);
    private readonly ViewSummaries views = new(model);
    private readonly ProcedureCalls procedures = new(model, declarations);

    /// <summary>What every body of the model binds to, shared by every body read, so each query is bound once.</summary>
    public Bindings Bindings { get; } = new(model);

    /// <summary>What the triggers a write fires may return, shared by every walk so that each gap is warned of once.</summary>
    public DmlTriggers Triggers { get; } = new(model, warnings);

    /// <summary>The object each recursive function's node stands for, settled at the fixed point.</summary>
    public IReadOnlyDictionary<ObjectName, ObjectShape> Nodes => functions.Nodes;

    /// <summary>
    /// What a call of the scalar function <paramref name="name"/> returns, or why it is not known; read uncommitted, the
    /// keys of what its body reads don't hold, and a recursive one gives its declared type only.
    /// </summary>
    public FunctionSummaries.OneOf Function(ObjectName name, SourceLocation at, bool uncommitted) =>
        (uncommitted ? uncommittedFunctions : functions).Find(name, at, this);

    /// <summary>What a view holds, read once per way of reading it.</summary>
    public LocalRelationValues View(View view, bool uncommitted) => views.Read(view, uncommitted, this);

    /// <summary>What a call of a table-valued function holds, with its parameters holding <paramref name="arguments"/>.</summary>
    public LocalRelationValues TableFunction(Function function, EquatableMap<LocalName, Value> arguments, bool uncommitted) =>
        views.Function(function, arguments, uncommitted, this);

    /// <summary>One procedure's results, as the outermost call.</summary>
    public ProcedureAnalysis Analyse(Procedure procedure) => procedures.Analyse(procedure, this);

    /// <summary>What a call of <paramref name="callee"/> from a caller in <paramref name="context"/> does.</summary>
    public Walked Follow(Procedure callee, CallContext context, SourceLocation at) => procedures.Follow(callee, context, at, this);

    /// <summary>The declaration of a procedure's results, if there is one, with the name it is declared by.</summary>
    public (ObjectName Name, DeclaredResults Results)? Declared(ObjectName name) => procedures.Declared(name);

    /// <summary>The temporary tables a procedure, or any it calls, may change (<see cref="ProcedureCalls.TempWrites"/>).</summary>
    public IReadOnlySet<LocalName> TempWrites(ObjectName name) => procedures.TempWrites(name);
}
