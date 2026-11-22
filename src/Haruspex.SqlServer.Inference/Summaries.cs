using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

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
/// <param name="session">The options the callers' connections are declared to set.</param>
/// <param name="columns">The columns declared to hold JSON of a schema.</param>
internal sealed class Summaries(DatabaseModel model, IReadOnlyDictionary<ObjectName, DeclaredResults> declarations, List<Diagnostic> warnings, SessionOptions session, IReadOnlyList<DeclaredColumn> columns)
{
    /// <summary>
    /// The options the callers' connections are declared to set, which every body runs under: a procedure, the procedures it
    /// calls, and the functions, views and triggers they reach all run in the caller's session.
    /// </summary>
    public SessionOptions Session { get; } = session;

    private readonly FunctionSummaries functions = new(model, uncommitted: false);
    private readonly FunctionSummaries uncommittedFunctions = new(model, uncommitted: true);
    private readonly ViewSummaries views = new(model);
    private readonly ProcedureCalls procedures = new(model, declarations);

    /// <summary>The columns declared to hold JSON of a schema, and which bodies read them, directly or through others.</summary>
    public DeclaredColumns DeclaredColumns { get; } = new(model, columns);

    /// <summary>What every body of the model binds to, shared by every body read, so each query is bound once.</summary>
    public Bindings Bindings { get; } = new(model);

    /// <summary>What the triggers a write fires may return, shared by every walk so that each gap is warned of once.</summary>
    public DmlTriggers Triggers { get; } = new(model, warnings);

    private readonly Dictionary<SourceLocation, Diagnostic> jsonText = [];

    private readonly HashSet<Diagnostic> warned = [];

    /// <summary>
    /// Adds a warning of what inference found wrong in a body, once, however often the body is walked (a JSON path that never
    /// finds anything, <see cref="InferenceCodes.PathFindsNothing"/>).
    /// </summary>
    public void Warn(Diagnostic warning)
    {
        if (warned.Add(warning))
            warnings.Add(warning);
    }

    /// <summary>
    /// The places a body reads JSON assembled as text, one note each (<see cref="JsonTextNotes"/>): those the SQL shows by its
    /// construction are found without typing, but text read through a variable, <c>'[' + @ids + ']'</c>, only by it.
    /// </summary>
    public IReadOnlyCollection<Diagnostic> JsonText => jsonText.Values;

    /// <summary>Records a place JSON assembled as text is read, once, however often its body is walked.</summary>
    /// <param name="at">Where the expression that assembles it starts.</param>
    /// <param name="expression">The expression.</param>
    /// <param name="aggregated">Whether its text holds a <c>string_agg</c>.</param>
    public void Assembled(SourceLocation at, ScalarExpression expression, bool aggregated) =>
        jsonText.TryAdd(at, JsonTextNotes.Note(at, expression, aggregated));

    /// <summary>The object each recursive function's node stands for, settled at the fixed point.</summary>
    public IReadOnlyDictionary<ObjectName, ObjectShape> Nodes => functions.Nodes;

    /// <summary>
    /// What a call of the scalar function <paramref name="name"/> returns, or why it is not known; read uncommitted, the
    /// keys of what its body reads don't hold, and a recursive one gives its declared type only.
    /// </summary>
    /// <param name="reader">The body that calls it, which reaches the function's.</param>
    public FunctionSummaries.OneOf Function(ObjectName name, SourceLocation at, bool uncommitted, SourceSpan reader)
    {
        if (model.FindFunction(name) is { } function)
            DeclaredColumns.Reached(reader, function.Span);
        return (uncommitted ? uncommittedFunctions : functions).Find(name, at, this);
    }

    /// <summary>What a view holds, read once per way of reading it.</summary>
    /// <param name="reader">The body that reads it, which reaches the view's.</param>
    public LocalRelationValues View(View view, bool uncommitted, SourceSpan reader)
    {
        DeclaredColumns.Reached(reader, view.Span);
        return views.Read(view, uncommitted, this);
    }

    /// <summary>What a call of a table-valued function holds, with its parameters holding <paramref name="arguments"/>.</summary>
    /// <param name="reader">The body that calls it, which reaches the function's.</param>
    public LocalRelationValues TableFunction(Function function, EquatableMap<LocalName, Value> arguments, bool uncommitted, SourceSpan reader)
    {
        DeclaredColumns.Reached(reader, function.Span);
        return views.Function(function, arguments, uncommitted, this);
    }

    /// <summary>One procedure's results, as the outermost call.</summary>
    public ProcedureAnalysis Analyse(Procedure procedure) => procedures.Analyse(procedure, this);

    /// <summary>What a call of <paramref name="callee"/> from a caller in <paramref name="context"/> does.</summary>
    /// <param name="reader">The body that calls it, which reaches the callee's.</param>
    public Walked Follow(Procedure callee, CallContext context, SourceLocation at, SourceSpan reader)
    {
        DeclaredColumns.Reached(reader, callee.Span);
        return procedures.Follow(callee, context, at, this);
    }

    /// <summary>The declaration of a procedure's results, if there is one, with the name it is declared by.</summary>
    public (ObjectName Name, DeclaredResults Results)? Declared(ObjectName name) => procedures.Declared(name);

    /// <summary>The temporary tables a procedure, or any it calls, may change (<see cref="ProcedureCalls.TempWrites(ObjectName)"/>).</summary>
    public IReadOnlySet<LocalName> TempWrites(ObjectName name) => procedures.TempWrites(name);

    /// <summary>The temporary tables a trigger, or any procedure it calls, may change (<see cref="ProcedureCalls.TempWrites(TSqlFragment, SourceSpan)"/>).</summary>
    public IReadOnlySet<LocalName> TempWrites(Trigger trigger) =>
        trigger.Body is SqlBody { Statements: var body } ? procedures.TempWrites(body, trigger.Span) : [];
}
