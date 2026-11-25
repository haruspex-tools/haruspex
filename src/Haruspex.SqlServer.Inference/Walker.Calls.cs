using System.Collections.Immutable;
using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using static Haruspex.SqlServer.Inference.Typer;

namespace Haruspex.SqlServer.Inference;

// EXEC and INSERT … EXEC. README.md in this folder ("Calls") gives the model, and the observations it rests on
// are ExecObservationTests in the differential tests.
internal sealed partial class Walker
{
    /// <summary>What each procedure called does, and what the triggers a write fires may return.</summary>
    private readonly Summaries summaries;

    /// <summary>Whether this is a procedure's walk, which follows the calls it makes and the triggers its writes fire; a function's follows neither.</summary>
    private readonly bool followsCalls;

    /// <summary>What this procedure's walk starts from: its caller's temporary tables, and how it was called.</summary>
    private readonly CallContext context;

    /// <summary>Every state an error may leave this procedure from, when a caller's CATCH may catch it; null otherwise.</summary>
    private readonly List<FlowState>? escapes;

    /// <summary>The procedures whose declared results were taken, at any depth of calls.</summary>
    private EquatableArray<ObjectName> declaredUsed = [];

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

        // WITH RESULT SETS (…) renames the callee's columns and converts its values, and NONE refuses a callee that returns a
        // result set (observed: With_result_sets_renames_and_converts_the_callees_columns_or_refuses_them). Neither is
        // modelled; UNDEFINED is as no clause.
        if (execute.Options.OfType<ResultSetsExecuteOption>().Any(o => o.ResultSetsOptionKind != ResultSetsOptionKind.Undefined))
        {
            blocker ??= new UnknownShape(
                IrUnknownReason.UnsupportedStatement,
                "EXEC … WITH RESULT SETS is not modelled yet, so the procedure's result sets cannot be listed.",
                At(execute));
            return Outcome.Ends;
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
    /// Into a permanent table it returns nothing either, unless a trigger it fires may, or it has an OUTPUT. Into a temporary
    /// table or a table variable, the rows are the callee's columns by position (<see cref="RowsReceived"/>): a column
    /// <c>SELECT … INTO</c> made joins with them, and a traced column holds the JSON they carry.
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
        var (fired, reached) = followsCalls ? summaries.Triggers.Check(written, binder, DmlEvents.Insert, at) : (null, []);
        if (fired is not null)
        {
            blocker ??= fired;
            return Outcome.Ends;
        }

        var called = Call(execute, state, inserting: true);
        if (called.Unknowable is { } why)
        {
            var forgotten = Assigned(execute, Forgotten(state, why, at));
            if (written.Direct is { IsVariable: true } variable)
                return Continue(TableVariableWritten(forgotten, variable, null, null, null, at, $"by a call whose result sets are not known: {why.Detail.TrimEnd('.')}"));
            return Continue(Output(insert, spec, null, null, insert.WithCtesAndXmlNamespaces, written, state, forgotten));
        }

        if (called.After is not { } after)
            return Outcome.Ends;
        after = Assigned(execute, after);
        if (written.Direct is not { } target)
            return Continue(Output(insert, spec, null, null, insert.WithCtesAndXmlNamespaces, written, state, Triggered(after, reached, at)));

        // Into a table variable: what its traced columns hold as JSON is what the callee's columns hold, by position.
        if (target.IsVariable)
        {
            var into = TableVariableTargets(target, spec.Columns);
            return Continue(TableVariableWritten(after, target, into, into is null ? null : RowsReceived(called.Received, into.Count, target), null, at));
        }

        if (!target.IsTemporary)
            return Continue(Triggered(after, reached, at));
        if (after.Temp(target) is not KnownTable table)
            return Continue(after);

        // Without a column list the rows go into every column but an identity or computed column (observed, also of an identity
        // column SELECT … INTO copied); when a column may be an identity column, which columns they go into is not known.
        if (spec.Columns.Count == 0 && table.Columns.FirstOrDefault(c => c.Identity is null) is { } maybe)
        {
            return Continue(after.WithTemp(target, Unknown(target, Unsupported(IrUnknownReason.UnsupportedStatement,
                $"{maybe.Name} may be an identity column SELECT … INTO copied, which the insert at {at} skips without a column list, so which columns its rows go into is not known.", at))));
        }

        var targets = spec.Columns.Count == 0
            ? [.. Enumerable.Range(0, table.Columns.Count).Where(i => table.Columns[i].Identity != true && !table.Columns[i].IsComputed)]
            : Binder.Written(spec.Columns, [.. table.Columns.Select(c => c.Name)]).ToList();
        if (targets.Any(i => i < 0))
            return Continue(after.WithTemp(target, Unknown(target, Unsupported(IrUnknownReason.UnresolvedColumn, "The insert names a column the table does not have.", at))));

        return Continue(after.WithTemp(target, Written(table, targets, RowsReceived(called.Received, targets.Count, target), at)));
    }

    /// <summary>
    /// The rows <c>INSERT … EXEC</c> puts in <paramref name="target"/> from what the callee returned: every rowset's, by
    /// position (observed: Insert_exec_maps_the_callees_columns_to_the_targets_by_position), one row for each shape a result
    /// set may have. A result set of another width than the insert's <paramref name="width"/> columns fails the insert, so a
    /// successful one never received it.
    /// </summary>
    private static List<IReadOnlyList<Value>> RowsReceived(ResultTrack received, int width, LocalName target)
    {
        var rows = new List<IReadOnlyList<Value>>();
        foreach (var produced in received.All.Where(p => p.Result.Encoding == IrEncoding.Rowset))
        {
            foreach (var shape in produced.Result.Shape is UnionShape union ? union.Variants : [produced.Result.Shape])
            {
                switch (shape)
                {
                    case ArrayShape { Items: ObjectShape row } when row.Fields.Count == width:
                        rows.Add([.. row.Fields.Select((f, n) => Received(f, n, produced.At, target))]);
                        break;
                    case ArrayShape { Items: ObjectShape }:
                        // Of another width: it fails the insert (213), so a successful one never received it.
                        break;
                    default:
                        var unknown = shape as UnknownShape ?? Unsupported(IrUnknownReason.UnsupportedStatement, $"The rows {produced.At} returns into {target} are not of a form modelled.", produced.At);
                        rows.Add([.. Enumerable.Range(0, width).Select(_ => Value.Unknown(unknown))]);
                        break;
                }
            }
        }

        return rows;
    }

    /// <summary>
    /// The value a callee's column puts in the row it inserts: its type and why it may be null. JSON text keeps its content,
    /// which a traced column of the target holds (<see cref="JsonSlot"/>); any other value names the column, for when it is
    /// read as JSON.
    /// </summary>
    /// <param name="field">The callee's column.</param>
    /// <param name="position">Its position in the result set, from 0.</param>
    /// <param name="at">The result set.</param>
    /// <param name="target">The table it goes into.</param>
    private static Value Received(FieldShape field, int position, SourceLocation at, LocalName target)
    {
        var value = Value.Computed(field.Type, field.NullableBecause);
        if (field.Type is JsonTextShape or UnknownShape)
            return value;
        var why = $"{target} receives {field.Name}, column {position + 1} of the result set at {at}, by INSERT … EXEC, and it is not JSON built there.";
        // A witness only: with keeps every other fact but the digits, which are set again.
        return value with { JsonWhy = Unsupported(IrUnknownReason.JsonShapeUnknown, why, at), Digits = value.Digits };
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

        // A multi-statement function's body is walked with no procedures to follow. SQL Server creates a function that calls a
        // procedure, and refuses the call when the function runs (557, observed:
        // A_function_that_calls_a_procedure_is_created_and_every_call_of_it_fails), so no call of it returns; an extended
        // stored procedure (xp_…) may be called there, and is not followed either. The walk stops at the call, so the function's
        // table is as declared, which claims nothing a call could contradict.
        if (!followsCalls)
            return new Called(null, ResultTrack.None, new UnknownShape(IrUnknownReason.UnsupportedStatement, $"{Text(execute)} calls a procedure from a function's body, which SQL Server refuses when the function runs (557); it is not followed.", at));
        var temps = state.Temps;
        var needEscapes = catchEntries.Count > 0 || escapes is not null;

        var (walked, name) = target switch
        {
            ProcedureCall { Procedure: var callee } when CaseVariantTemps(callee, state, at) is { } variants => (new Walked(null, [], variants, []), (ObjectName?)callee.Name),
            ProcedureCall { Procedure: var callee } => (summaries.Follow(callee, new CallContext(temps, needEscapes, inserting, state.ReadUncommitted), at, source), (ObjectName?)callee.Name),
            UnknowableCall unknowable => (new Walked(null, [], new UnknownShape(Reason(unknowable.Kind), unknowable.Detail, at), []), unknowable.Name),
            SystemProcedureCall { Procedure: var system } => (SystemCalled(system, state, needEscapes, Text(execute), at), (ObjectName?)system.Name),
        };

        if (walked.Blocker is { } why)
        {
            // Its results cannot be known; a declaration of them stands in for them. What it did to the caller's tables is not
            // known either way: the declaration says nothing of them.
            if (name is { } calledName && summaries.Declared(calledName) is var (declaredName, declaration))
            {
                declaredUsed = Walked.Union(declaredUsed, [declaredName]);
                var forgotten = Forgotten(state, new UnknownShape(why.Reason, $"{declaredName}'s results are declared, but what it does to temporary tables is not known: {why.Detail}", why.At), at);
                var received = Declared(declaration, declaredName, at);
                if (needEscapes)
                    Escaped([forgotten.WithResults(inserting ? state.Results : state.Results.Then(received.Prefixes()))]);
                return new Called(forgotten, received, null);
            }

            var through = target is ProcedureCall ? why with { Detail = $"{name} is called at {at}: {why.Detail}" } : why;
            return new Called(null, ResultTrack.None, through);
        }

        declaredUsed = Walked.Union(declaredUsed, walked.Declared);
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

    /// <summary>
    /// What a call of one of SQL Server's own procedures the catalogue lists does (<see cref="SystemProcedures"/>): it changes none
    /// of the caller's tables and returns no result set, or its result sets are unknown, saying why. It may fail, leaving the
    /// state as it was.
    /// </summary>
    private static Walked SystemCalled(SystemProcedure system, FlowState state, bool needEscapes, string call, SourceLocation at)
    {
        if (system.ResultsUnknownBecause is { } unknown)
            return new Walked(null, [], new UnknownShape(IrUnknownReason.SystemProcedure, $"{call} runs {system.Name}, whose result sets are not known: {unknown}.", at), []);
        var unchanged = state.WithResults(ResultTrack.None);
        return new Walked(unchanged, needEscapes ? [unchanged] : [], null, []);
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
    private static FlowState Returned(FlowState caller, FlowState callee, ImmutableHashSet<LocalName> own, SourceLocation at)
    {
        var temps = caller.Temps;
        foreach (var (name, table) in callee.Temps)
        {
            if (own.Contains(name))
            {
                if (caller.Temps.ContainsKey(name))
                {
                    temps = temps.SetItem(name, new UnknownTable(new UnknownShape(
                        IrUnknownReason.UnsupportedStatement,
                        $"The procedure called at {at} creates a temporary table named {name} too, and what it does to the caller's before that is not followed.",
                        at)));
                }

                continue;
            }

            temps = temps.SetItem(name, table);
        }

        return caller.WithTemps(temps);
    }

    /// <summary>
    /// Why a call isn't followed when the callee creates a temporary table whose name differs only in case from one the caller
    /// holds (<c>#T</c> and <c>#t</c>); null when it creates none. A callee reads its caller's temporary tables, matched by the
    /// server's collation: on a case-insensitive server its <c>#t</c> is its own <c>#T</c> (observed:
    /// A_callee_s_temporary_table_hides_its_caller_s_of_a_name_that_differs_only_in_case), on a case-sensitive one the caller's.
    /// Unlike two such names in one body, the pair proves nothing of the collation, so it is no error (decision 0119), and the
    /// call is unknown rather than typed as either.
    /// </summary>
    private static UnknownShape? CaseVariantTemps(Procedure callee, FlowState state, SourceLocation at)
    {
        foreach (var created in Created(callee))
        {
            foreach (var (held, table) in state.Temps)
            {
                if (table is not AbsentTable && held == created && !string.Equals(held.Text, created.Text, StringComparison.Ordinal))
                {
                    return new UnknownShape(
                        IrUnknownReason.UnsupportedStatement,
                        $"{callee.Name} creates the temporary table {created}, whose name differs only in case from the caller's {held}: which of the two each name in it means depends on the server's collation, so the call is not followed.",
                        at);
                }
            }
        }

        return null;
    }

    /// <summary>The temporary tables a procedure creates anywhere in its body: its own, gone when it returns.</summary>
    private static ImmutableHashSet<LocalName> Created(Procedure procedure)
    {
        var finder = new CreateFinder();
        (procedure.Body as SqlBody)?.Statements.Accept(finder);
        return finder.Names.ToImmutable();
    }

    private sealed class CreateFinder : TSqlFragmentVisitor
    {
        public ImmutableHashSet<LocalName>.Builder Names { get; } = ImmutableHashSet.CreateBuilder<LocalName>();

        public override void Visit(CreateTableStatement node) => Add(node.SchemaObjectName);

        public override void Visit(SelectStatement node)
        {
            if (node.Into is not null)
                Add(node.Into);
        }

        private void Add(SchemaObjectName name)
        {
            if (Binder.Temporary(name) is { } created)
                Names.Add(created);
        }
    }

    /// <summary>
    /// The state after the triggers a write may fire (<see cref="DmlTriggers.Check"/>) have run: each temporary table one of them,
    /// or a procedure it calls, may write, alter or drop is unknown, since a trigger runs in the session that fired it, so its
    /// <c>#t</c> is the procedure's (observed: A_trigger_changes_the_temporary_table_of_the_procedure_whose_write_fires_it).
    /// </summary>
    private FlowState Triggered(FlowState state, IReadOnlyList<Trigger> reached, SourceLocation at)
    {
        foreach (var trigger in reached)
        {
            foreach (var name in summaries.TempWrites(trigger).Where(n => state.Temp(n) is KnownTable))
            {
                state = state.WithTemp(name, new UnknownTable(new UnknownShape(
                    IrUnknownReason.UnsupportedStatement,
                    $"{name} may be changed by the trigger {trigger.Name}, which the write at {at} may fire.",
                    at)));
            }
        }

        return state;
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
            state = Assign(state, Resolved(returned).Name, Nulls(Resolved(returned).Name, []), definitely: true, at);
        foreach (var parameter in execute.ExecutableEntity?.Parameters ?? [])
        {
            if (parameter.IsOutput && parameter.ParameterValue is VariableReference output)
            {
                // The reason names the callee's parameter as the call writes it (@p = @v OUTPUT), or the call.
                var variable = Resolved(output).Name;
                state = Assign(state, variable, Nulls(variable, [new IrNullReason(IrNullRule.Parameter, FragmentText.OneLine((TSqlFragment?)parameter.Variable ?? execute))]), definitely: true, at);
            }
        }

        return state;

        // Assign keeps the variable's declared type, which the value is converted to; only why it may be null is taken.
        Value Nulls(LocalName name, EquatableArray<IrNullReason> because) =>
            Value.Computed(variableShapes.TryGetValue(name, out var shape) ? shape : new NeverShape(at), because);
    }

    /// <summary>The result sets a declaration gives, as one path returning each in turn, from the call at <paramref name="at"/>.</summary>
    private static ResultTrack Declared(DeclaredResults declaration, ObjectName name, SourceLocation at)
    {
        var track = ResultTrack.None;
        foreach (var result in declaration.Results)
            track = track.Append(new ResultShape(result.Encoding, IrShapes.ToShape(result.Type, declaration.Types, $"The declared results of {name}", at), result.AbsentBecause, result.Cardinality), at);
        return track;
    }

    private static IrUnknownReason Reason(UnknowableCallKind kind) => kind switch
    {
        UnknowableCallKind.DynamicSql => IrUnknownReason.DynamicSql,
        UnknowableCallKind.ProcedureVariable => IrUnknownReason.ProcedureVariable,
        UnknowableCallKind.SystemProcedure => IrUnknownReason.SystemProcedure,
        UnknowableCallKind.MissingProcedure => IrUnknownReason.MissingProcedure,
        UnknowableCallKind.AmbiguousProcedure => IrUnknownReason.AmbiguousProcedure,
    };
}
