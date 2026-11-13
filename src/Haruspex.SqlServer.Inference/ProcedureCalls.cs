using System.Collections.Immutable;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Follows <c>EXEC</c> into the procedures of the project. A callee is walked again at each call, from its caller's
/// temporary tables at that point, so that what it reads of them and does to them is known; its parameters are its declared
/// types, nullable, which holds for every call. Calls that form a cycle are solved as a fixed point. README.md in this
/// folder ("Calls") gives the model and the observations it rests on.
/// The idea, and the alternatives, are in docs/internals/07-summaries-and-recursion.md.
/// </summary>
/// <remarks>
/// <para>
/// <b>Recursion.</b> A call of a procedure that is already being walked (it calls itself, directly or through others) does
/// not walk it again. It gives what is assumed of that procedure: at first that it never returns (the path through the call
/// ends), then the result sets its last walk returned, until a walk returns what was assumed. The walk starts from the join
/// of the temporary tables it was called with and those every recursive call of it starts from (the procedure may have
/// written them first), widened and walked again until no call adds to them, so the assumption holds for each call's tables.
/// With monotone steps result sets only grow from one walk to the next, so this reaches the least fixed point, which
/// describes every call that returns, by induction on its depth. At most <see cref="FixedPoint.MostPasses"/> walks are taken (<see cref="FixedPoint.Iterate"/>), which is
/// what makes it end; a procedure that returns more result sets at each depth never settles,
/// and is unknown (<c>unresolved-recursion</c>). What the recursive call does to temporary tables is not iterated: every
/// table that the procedures it can reach write is unknown after it. SQL Server stops at 32 nested calls (error 217,
/// observed), which fails the call, so it describes no result the IR has to.
/// </para>
/// <para>
/// <b>Cache.</b> The walk of a callee from the same temporary tables, with the same flags, gives the same outcome, unless it
/// rested on an assumption about a procedure further down the stack of calls; those are walked again.
/// </para>
/// </remarks>
internal sealed class ProcedureCalls(DatabaseModel model, FunctionSummaries functions, IReadOnlyDictionary<ObjectName, DeclaredResults> declarations, DmlTriggers triggers, ViewSummaries views)
{
    /// <summary>What the triggers a write fires may return, shared by every walk so that each gap is warned of once.</summary>
    public DmlTriggers Triggers => triggers;

    /// <summary>What a recursive procedure's walk assumes: the result sets its calls of itself return (none: they never return), and the tables it starts from.</summary>
    private sealed record Assumption(ResultTrack? Results, CallContext Context);

    private sealed class Frame(ObjectName name, CallContext context)
    {
        public ObjectName Name { get; } = name;

        /// <summary>
        /// What its walk starts from: the context it was called with, joined with the temporary tables of every recursive
        /// call of it met, so that what is assumed of those calls holds for the tables they start from.
        /// </summary>
        public CallContext Context { get; set; } = context;

        /// <summary>Whether a recursive call widened <see cref="Context"/> in this pass, so another is needed.</summary>
        public bool Widened { get; set; }

        /// <summary>Whether a call of it was met while it was being walked, in this pass.</summary>
        public bool Recursive { get; set; }

        /// <summary>Whether its walk took an assumption about a procedure below it on the stack, so it cannot be cached.</summary>
        public bool Dependent { get; set; }
    }

    private readonly List<Frame> stack = [];

    /// <summary>The result sets assumed of each recursive procedure being solved; absent while it is assumed never to return.</summary>
    private readonly Dictionary<ObjectName, ResultTrack> assumed = [];
    /// <summary>
    /// Each walk that depends on nothing but its procedure and context (<see cref="Frame.Dependent"/>): a walk of the same
    /// procedure from an equal context, its temporary tables compared by value, is the same walk.
    /// </summary>
    private readonly Dictionary<(ObjectName Procedure, CallContext Context), Walked> cache = [];
    private readonly Dictionary<ObjectName, ImmutableHashSet<LocalName>> writes = [];

    public DatabaseModel Model => model;

    /// <summary>The declaration of a procedure's results, if there is one, with the name it is declared by.</summary>
    public (ObjectName Name, DeclaredResults Results)? Declared(ObjectName name) =>
        declarations.Keys.Where(k => k == name).Select(k => ((ObjectName, DeclaredResults)?)(k, declarations[k])).FirstOrDefault();

    /// <summary>Infers one procedure's results, as the outermost call.</summary>
    public ProcedureAnalysis Analyse(Procedure procedure)
    {
        var parameters = ProcedureAnalyzer.Parameters(model, procedure);
        if (Unwalkable(procedure) is { } why)
            return new ProcedureAnalysis([.. parameters], null, why, []);

        var walked = Run(procedure, CallContext.Top);
        if (walked.Blocker is { } blocker)
            return new ProcedureAnalysis([.. parameters], null, blocker, walked.Declared);
        if (walked.End is null)
            return new ProcedureAnalysis([.. parameters], [], null, walked.Declared);
        var (results, varying) = ProcedureAnalyzer.Results(walked.End.Results);
        return new ProcedureAnalysis([.. parameters], results is null ? null : [.. results], varying, walked.Declared);
    }

    /// <summary>What a call of <paramref name="callee"/> from a caller in <paramref name="context"/> does.</summary>
    public Walked Follow(Procedure callee, CallContext context, SourceLocation at)
    {
        if (Unwalkable(callee) is { } why)
            return new Walked(null, [], why, []);

        var index = stack.FindIndex(f => f.Name == callee.Name);
        if (index < 0)
            return Run(callee, context);

        // A recursive call: what is assumed of the callee, and every frame above it rests on that assumption.
        var frame = stack[index];
        frame.Recursive = true;
        for (var i = index + 1; i < stack.Count; i++)
            stack[i].Dependent = true;

        // The assumption is the callee's results from the tables its walk starts from. The recursive call may start from
        // others (the procedure has written them), so the walk starts from the join of both, again, until it holds.
        if (context.Inserting != frame.Context.Inserting)
        {
            return new Walked(null, [], new UnknownShape(
                IrUnknownReason.UnresolvedRecursion,
                $"{callee.Name} calls itself under INSERT … EXEC and outside it, which is not modelled.",
                at), []);
        }

        var joined = FlowState.Join(FlowState.Start([]).WithTemps(frame.Context.Temps), FlowState.Start([]).WithTemps(context.Temps), at)!.Temps;
        var wider = frame.Context with { Temps = joined, ReadUncommitted = frame.Context.ReadUncommitted || context.ReadUncommitted };
        if (!wider.Equals(frame.Context))
        {
            frame.Context = wider;
            frame.Widened = true;
        }

        return Assumed(callee, context, at);
    }

    /// <summary>Why a procedure cannot be walked at all: a CLR procedure, or one changed after its definition.</summary>
    private static UnknownShape? Unwalkable(Procedure procedure)
    {
        if (procedure.Body is not SqlBody)
            return new UnknownShape(IrUnknownReason.ExternalCode, $"{procedure.Name} is a CLR procedure: its results come from .NET code, which is not in the project.", procedure.Span.Start);

        // The parameters are still read from the definition the model kept, but which body runs depends on the order the
        // scripts run, so the results are withdrawn.
        return procedure.Redefinition is { } redefinition ? new UnknownShape(IrUnknownReason.Redefined, Redefinitions.Why(redefinition), redefinition.At) : null;
    }

    private Walked Run(Procedure procedure, CallContext context)
    {
        if (cache.TryGetValue((procedure.Name, context), out var cached))
            return cached;

        // Only a procedure Unwalkable passes is run.
        var statements = procedure.Body is SqlBody body ? body.Statements : throw new ArgumentException($"{procedure.Name} has no T-SQL body to walk.", nameof(procedure));

        var frame = new Frame(procedure.Name, context);
        stack.Add(frame);
        try
        {
            // The least fixed point of a walk, over what the recursive calls are assumed to return and the tables they start
            // from. A walk that meets no call of itself, or can't be listed, is the answer at once.
            Walked walked = null!;
            var iteration = FixedPoint.Iterate(
                new Assumption(null, context),
                assumption =>
                {
                    if (assumption.Results is null)
                        assumed.Remove(procedure.Name);
                    else
                        assumed[procedure.Name] = assumption.Results;
                    frame.Context = assumption.Context;
                    frame.Recursive = false;
                    frame.Widened = false;
                    walked = new ProcedureAnalyzer(model, procedure.Span, functions, calls: this, context: frame.Context, ansiNullsOff: procedure.AnsiNullsOff, views: views).Walk(procedure.Parameters, statements);
                    if (!frame.Recursive || walked.Blocker is not null)
                        return Pass<Assumption>.Stop();

                    // A call of itself was met: settled when it returned what the call was assumed to, from tables that cover
                    // the call's. When a call widened the tables, what was assumed was read from narrower ones, so the next
                    // walk assumes nothing again, from the wider ones.
                    return Pass<Assumption>.To(new Assumption(frame.Widened ? null : walked.End?.Results, frame.Context));
                });

            if (iteration.End != IterationEnd.Unsettled)
            {
                if (!frame.Dependent)
                    cache[(procedure.Name, context)] = walked;
                return walked;
            }

            return new Walked(null, [], new UnknownShape(
                IrUnknownReason.UnresolvedRecursion,
                $"{procedure.Name} calls itself, directly or through other procedures, and the result sets it returns reached no fixed point in {FixedPoint.MostPasses} walks: it may return more at each depth.",
                procedure.Span.Start), []);
        }
        finally
        {
            stack.RemoveAt(stack.Count - 1);
            assumed.Remove(procedure.Name);
        }
    }

    /// <summary>
    /// A recursive call: the result sets assumed of the callee (none at first, so the path through the call ends), and every
    /// temporary table that it, or a procedure it can reach, writes made unknown.
    /// </summary>
    private Walked Assumed(Procedure callee, CallContext context, SourceLocation at)
    {
        var written = Writes(callee.Name);
        var temps = context.Temps;
        foreach (var name in temps.Keys.Where(written.Contains).ToList())
        {
            temps = temps.SetItem(name, new UnknownTable(new UnknownShape(
                IrUnknownReason.UnresolvedRecursion,
                $"{name} may be changed by the recursive call of {callee.Name} at {at}, which is not followed.",
                at)));
        }

        var state = FlowState.Start([]).WithTemps(temps);
        var track = assumed.GetValueOrDefault(callee.Name);
        IReadOnlyList<FlowState> escapes = context.Escapes ? [state.WithResults((track ?? ResultTrack.None).Prefixes())] : [];
        return new Walked(track is null ? null : state.WithResults(track), [.. escapes], null, []);
    }

    /// <summary>
    /// The temporary tables a procedure writes, creates, alters or drops, or any procedure it calls does, at any depth: what
    /// a call of it may change, read from the bodies alone.
    /// </summary>
    public ImmutableHashSet<LocalName> TempWrites(ObjectName name) => Writes(name);

    /// <inheritdoc cref="TempWrites"/>
    private ImmutableHashSet<LocalName> Writes(ObjectName name)
    {
        if (writes.TryGetValue(name, out var known))
            return known;
        writes[name] = [];
        var all = ImmutableHashSet<LocalName>.Empty;
        if (model.FindProcedure(name) is { Body: SqlBody { Statements: var body } } procedure)
        {
            var finder = new WriteFinder();
            body.Accept(finder);
            all = all.Union(finder.Tables);
            var binder = new Binder(model, procedure.Span);
            foreach (var call in finder.Calls)
            {
                if (binder.BindCall(call) is ProcedureCall { Procedure: var callee })
                    all = all.Union(Writes(callee.Name));
            }
        }

        writes[name] = all;
        return all;
    }

    private sealed class WriteFinder : TSqlFragmentVisitor
    {
        public HashSet<LocalName> Tables { get; } = [];

        public List<ExecuteSpecification> Calls { get; } = [];

        // A write's target as DmlTarget resolves it: directly, or through an alias, a CTE or a derived table.
        public override void Visit(InsertStatement node) => Add(DmlTarget.Of(node.InsertSpecification.Target, null, node.WithCtesAndXmlNamespaces));

        public override void Visit(UpdateStatement node) => Add(DmlTarget.Of(node.UpdateSpecification.Target, node.UpdateSpecification.FromClause, node.WithCtesAndXmlNamespaces));

        public override void Visit(MergeStatement node) => Add(DmlTarget.Of(node.MergeSpecification.Target, null, node.WithCtesAndXmlNamespaces));

        public override void Visit(OutputIntoClause node) => Add(node.IntoTable);

        public override void Visit(CreateTableStatement node) => Add(node.SchemaObjectName);

        public override void Visit(AlterTableStatement node) => Add(node.SchemaObjectName);

        public override void Visit(DropTableStatement node)
        {
            foreach (var name in node.Objects)
                Add(name);
        }

        public override void Visit(SelectStatement node)
        {
            if (node.Into is not null)
                Add(node.Into);
        }

        public override void Visit(ExecuteSpecification node) => Calls.Add(node);

        private void Add(DmlTarget target)
        {
            foreach (var name in target.Temps)
                Tables.Add(name);
            if (target.Direct is { IsTemporary: true } direct)
                Tables.Add(direct);
        }

        private void Add(TableReference target)
        {
            foreach (var name in DmlTarget.NamesRead(target))
                Add(name);
        }

        private void Add(SchemaObjectName name)
        {
            // A caller's state keys only local temporary tables, so only those can be written there.
            if (Binder.Temporary(name) is { } temporary)
                Tables.Add(temporary);
        }
    }
}
