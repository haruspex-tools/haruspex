using System.Collections.Immutable;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// EXEC and INSERT … EXEC. README.md in this folder ("Calls") gives the model, and the observations it rests on
// are ExecObservationTests in the differential tests.
internal sealed partial class ProcedureAnalyzer
{
    private readonly ProcedureCalls? calls;

    /// <summary>What this procedure's walk starts from: its caller's temporary tables, and how it was called.</summary>
    private readonly CallContext context;

    /// <summary>Every state an error may leave this procedure from, when a caller's CATCH may catch it; null otherwise.</summary>
    private readonly List<FlowState>? escapes;

    /// <summary>The procedures whose declared results were taken, at any depth of calls.</summary>
    private ImmutableSortedSet<string> declaredUsed = ImmutableSortedSet.Create<string>(StringComparer.Ordinal);

    /// <summary>What a call gave: the caller's state after it, before its result sets are placed, and those result sets.</summary>
    /// <param name="After">The caller's state once the callee has returned, its temporary tables as the callee left them; null when no call returns.</param>
    /// <param name="Received">The result sets the callee returned, in its order.</param>
    /// <param name="Unknowable">Why nothing is known of what the call did, when that is so; then the others are not set.</param>
    private sealed record Called(FlowState? After, ResultTrack Received, UnknownShape? Unknowable);

    /// <summary>
    /// <c>EXEC</c>: the callee's result sets arrive where it is called, in order (observed), and its writes to the caller's
    /// temporary tables stay. A call whose results cannot be known makes the procedure's unknown, unless its results are
    /// declared; under <c>INSERT … EXEC</c> its results go into the table instead, so only the tables become unknown.
    /// </summary>
    private Outcome Execute(ExecuteStatement execute, FlowState state)
    {
        var called = Call(execute.ExecuteSpecification, state);
        if (called.Unknowable is { } why)
        {
            if (!context.Inserting)
            {
                blocker ??= why;
                return Outcome.Ends;
            }

            return Continue(Assigned(execute.ExecuteSpecification, Forgotten(state, why, At(execute))));
        }

        if (called.After is not { } after)
            return Outcome.Ends;
        if (called.Received.Positions.Count > 0)
            appended++;
        return Continue(Assigned(execute.ExecuteSpecification, after.WithResults(after.Results.Then(called.Received))));
    }

    /// <summary>
    /// <c>INSERT … EXEC</c>: the rows of every result set the callee returns go into the table, and none reaches the client
    /// (observed). A result set of another width than the insert's columns fails the statement, even with no rows (213), and
    /// so does a FOR JSON query (13602), both observed, so a successful insert received neither and nothing of them is written.
    /// Into a permanent table it returns nothing either, unless a trigger it fires may, or it has an OUTPUT.
    /// </summary>
    private Outcome InsertExecute(InsertStatement insert, FlowState state)
    {
        var spec = insert.InsertSpecification;
        var execute = ((ExecuteInsertSource)spec.InsertSource).Execute;
        var at = At(insert);
        var written = DmlTarget.Of(spec.Target, null, insert.WithCtesAndXmlNamespaces);
        if (written.Unmodelled is not null || (written.Direct is not null && (spec.OutputClause is not null || spec.OutputIntoClause is not null)))
            return Blocked(insert);

        // Into a permanent table: what its triggers may return (DmlTriggers), then the rows of its OUTPUT, if it has one.
        if (calls?.Triggers.Check(written, binder, DmlEvents.Insert, at) is { } fired)
        {
            blocker ??= fired;
            return Outcome.Ends;
        }

        var called = Call(execute, state, inserting: true);
        if (called.Unknowable is { } why)
            return Continue(Output(insert, spec, null, null, insert.WithCtesAndXmlNamespaces, written, state, Assigned(execute, Forgotten(state, why, at))));
        if (called.After is not { } after)
            return Outcome.Ends;
        after = Assigned(execute, after);
        if (written.Direct is null)
            return Continue(Output(insert, spec, null, null, insert.WithCtesAndXmlNamespaces, written, state, after));
        if (written.Direct.StartsWith('@'))
            return Continue(TableVariableWritten(after, written.Direct, null, null, null, at));
        var target = written.Direct.StartsWith('#') ? written.Direct : null;
        if (target is null || after.Temp(target) is not KnownTable table)
            return Continue(after);

        // Without a column list the rows go into every column but an identity column (observed, also of one SELECT … INTO
        // copied); when a column may be one, which columns they go into is not known.
        if (spec.Columns.Count == 0 && table.Columns.FirstOrDefault(c => c.Identity is null) is { } maybe)
        {
            return Continue(after.WithTemp(target, Unknown(target, Unsupported(IrUnknownReasons.UnsupportedStatement,
                $"{maybe.Name} may be an identity column SELECT … INTO copied, which the insert at {at} skips without a column list, so which columns its rows go into is not known.", at))));
        }

        var targets = spec.Columns.Count == 0
            ? [.. Enumerable.Range(0, table.Columns.Count).Where(i => table.Columns[i].Identity != true)]
            : spec.Columns.Select(c => IndexOf(table, c.MultiPartIdentifier.Identifiers[^1].Value)).ToList();
        if (targets.Any(i => i < 0))
            return Continue(after.WithTemp(target, Unknown(target, Unsupported(IrUnknownReasons.UnresolvedColumn, "The insert names a column the table does not have.", at))));

        var rows = new List<IReadOnlyList<Value>>();
        foreach (var produced in called.Received.All.Where(p => p.Result.Encoding == IrEncoding.Rowset))
        {
            foreach (var shape in produced.Result.Shape is UnionShape union ? union.Variants : [produced.Result.Shape])
            {
                switch (shape)
                {
                    case ArrayShape { Items: ObjectShape row } when row.Fields.Count == targets.Count:
                        rows.Add([.. row.Fields.Select(f => new Value(f.Type, f.NullableBecause))]);
                        break;
                    case ArrayShape { Items: ObjectShape }:
                        // Of another width: it fails the insert (213), so a successful one never received it.
                        break;
                    default:
                        var unknown = shape as UnknownShape ?? Unsupported(IrUnknownReasons.UnsupportedStatement, $"The rows {produced.At} returns into {target} are not of a form modelled.", produced.At);
                        rows.Add([.. targets.Select(_ => Value.Unknown(unknown))]);
                        break;
                }
            }
        }

        return Continue(after.WithTemp(target, Written(table, targets, rows, at)));
    }

    /// <summary>
    /// Follows the call <paramref name="execute"/> makes from <paramref name="state"/>. A procedure of the project is walked
    /// from the caller's temporary tables; a procedure whose results are declared gives those; anything else is unknowable.
    /// </summary>
    /// <param name="inserting">Whether its results go into a table (<c>INSERT … EXEC</c>), here or in a caller.</param>
    private Called Call(ExecuteSpecification execute, FlowState state, bool inserting = false)
    {
        inserting |= context.Inserting;
        var target = binder.BindCall(execute);
        var at = target.At;
        var temps = state.Temps;
        var needEscapes = catchEntries.Count > 0 || escapes is not null;

        Walked walked;
        ObjectName? name;
        switch (target)
        {
            case ProcedureCall { Procedure: var callee }:
                walked = calls!.Follow(callee, new CallContext(temps, needEscapes, inserting, state.ReadUncommitted), at);
                name = callee.Name;
                break;
            case UnknowableCall unknowable:
                walked = new Walked(null, [], new UnknownShape(Reason(unknowable.Kind), unknowable.Detail, at), []);
                name = unknowable.Name;
                break;
            default:
                throw new InvalidOperationException($"Unknown call target {target}.");
        }

        if (walked.Blocker is { } why)
        {
            // Its results cannot be known; a declaration of them stands in for them. What it did to the caller's tables is not
            // known either way: the declaration says nothing of them.
            if (name is { } calledName && calls!.Declared(calledName) is var (declaredName, declaration))
            {
                declaredUsed = declaredUsed.Add(declaredName.ToString());
                var forgotten = Forgotten(state, new UnknownShape(why.Reason, $"{declaredName}'s results are declared, but what it does to temporary tables is not known: {why.Detail}", why.At), at);
                var received = Declared(declaration, declaredName, at);
                if (needEscapes)
                    Escaped([forgotten.WithResults(inserting ? state.Results : state.Results.Then(received.Prefixes()))]);
                return new Called(forgotten, received, null);
            }

            var through = target is ProcedureCall ? why with { Detail = $"{name} is called at {at}: {why.Detail}" } : why;
            return new Called(null, ResultTrack.None, through);
        }

        declaredUsed = declaredUsed.Union(walked.Declared);
        var own = target is ProcedureCall call ? Created(call.Procedure) : [];
        if (needEscapes)
        {
            Escaped([.. walked.Escapes.Select(e =>
            {
                var back = Returned(state, e, own, at);
                return inserting ? back : back.WithResults(state.Results.Then(e.Results));
            })]);
        }
        return walked.End is { } end
            ? new Called(Returned(state, end, own, at), end.Results, null)
            : new Called(null, ResultTrack.None, null);
    }

    /// <summary>States an error inside a call may leave it from: they reach this procedure's CATCH, or leave it too.</summary>
    private void Escaped(IReadOnlyList<FlowState> states)
    {
        if (catchEntries.Count > 0)
            catchEntries.Peek().AddRange(states);
        escapes?.AddRange(states);
    }

    /// <summary>
    /// The caller's state once a callee returns from <paramref name="callee"/>: the caller's variables and results, and each
    /// temporary table as the callee left it. A table the callee created is dropped when it returns (observed), so it never
    /// reaches the caller; but a callee that creates a table of a name its caller has may have written the caller's before,
    /// which is not followed, so the caller's is unknown.
    /// </summary>
    private static FlowState Returned(FlowState caller, FlowState callee, ImmutableHashSet<string> own, SourceLocation at)
    {
        var temps = caller.Temps;
        foreach (var (name, table) in callee.Temps)
        {
            if (own.Contains(name))
            {
                if (caller.Temps.ContainsKey(name))
                {
                    temps = temps.SetItem(name, new UnknownTable(new UnknownShape(
                        IrUnknownReasons.UnsupportedStatement,
                        $"The procedure called at {at} creates a temporary table named {name} too, and what it does to the caller's before that is not followed.",
                        at)));
                }

                continue;
            }

            temps = temps.SetItem(name, table);
        }

        return caller.WithTemps(temps);
    }

    /// <summary>The temporary tables a procedure creates anywhere in its body: its own, gone when it returns.</summary>
    private static ImmutableHashSet<string> Created(Procedure procedure)
    {
        var finder = new CreateFinder();
        procedure.Body.StatementList?.Accept(finder);
        return finder.Names.ToImmutable();
    }

    private sealed class CreateFinder : TSqlFragmentVisitor
    {
        public ImmutableHashSet<string>.Builder Names { get; } = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);

        public override void Visit(CreateTableStatement node) => Add(node.SchemaObjectName);

        public override void Visit(SelectStatement node)
        {
            if (node.Into is not null)
                Add(node.Into);
        }

        private void Add(SchemaObjectName name)
        {
            if (IsTemporary(name))
                Names.Add(name.BaseIdentifier.Value);
        }
    }

    /// <summary>
    /// Every temporary table unknown: after a call nothing is known of, which can create, write, alter or drop any of them
    /// (observed of dynamic SQL), its caller's included.
    /// </summary>
    /// <param name="call">The call, in this procedure, which the detail names.</param>
    private static FlowState Forgotten(FlowState state, UnknownShape why, SourceLocation call)
    {
        var temps = state.Temps;
        foreach (var name in state.Temps.Keys)
            temps = temps.SetItem(name, new UnknownTable(why with { Detail = $"{name} may be changed by the call at {call}, which is not followed: {why.Detail}" }));
        return state.WithTemps(temps);
    }

    /// <summary>
    /// The variables a call sets: its return code (<c>EXEC @rc = …</c>), never null after a call that succeeds (observed:
    /// RETURN NULL gives 0), and each argument passed <c>OUTPUT</c>, which holds what the callee set or what was passed in
    /// (observed), so its declared type, and null as far as is known.
    /// </summary>
    private FlowState Assigned(ExecuteSpecification execute, FlowState state)
    {
        var at = At(execute);
        if (execute.Variable is { } returned)
            state = Assign(state, returned.Name, Nulls(returned.Name, []), definitely: true, at);
        foreach (var parameter in execute.ExecutableEntity?.Parameters ?? [])
        {
            if (parameter.IsOutput && parameter.ParameterValue is VariableReference output)
                state = Assign(state, output.Name, Nulls(output.Name, [new IrNullReason(IrNullRules.Parameter, parameter.Variable?.Name ?? FragmentText.OneLine(execute))]), definitely: true, at);
        }

        return state;

        // Assign keeps the variable's declared type, which the value is converted to; only why it may be null is taken.
        Value Nulls(string name, IReadOnlyList<IrNullReason> because) =>
            new(variableShapes.TryGetValue(name, out var shape) ? shape : new NeverShape(at), because);
    }

    /// <summary>The result sets a declaration gives, as one path returning each in turn, from the call at <paramref name="at"/>.</summary>
    private static ResultTrack Declared(DeclaredResults declaration, ObjectName name, SourceLocation at)
    {
        var track = ResultTrack.None;
        foreach (var result in declaration.Results)
            track = track.Append(new ResultShape(result.Encoding, IrShapes.ToShape(result.Type, declaration.Types, name, at), result.AbsentBecause, result.Cardinality), at);
        return track;
    }

    private static string Reason(UnknowableCallKind kind) => kind switch
    {
        UnknowableCallKind.DynamicSql => IrUnknownReasons.DynamicSql,
        UnknowableCallKind.ProcedureVariable => IrUnknownReasons.ProcedureVariable,
        UnknowableCallKind.SystemProcedure => IrUnknownReasons.SystemProcedure,
        UnknowableCallKind.MissingProcedure => IrUnknownReasons.MissingProcedure,
        UnknowableCallKind.AmbiguousProcedure => IrUnknownReasons.AmbiguousProcedure,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown kind of call."),
    };
}
