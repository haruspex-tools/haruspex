using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using static Haruspex.SqlServer.Inference.Typer;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The statement semantics of a body: its statements walked in order, carrying a <see cref="FlowState"/> from each to the
/// next, joining the states where paths meet, iterating a loop to a fixed point, and placing result sets where they are
/// returned. What an expression or query gives is the <see cref="Typer"/>'s, asked with the state the statement starts from;
/// what a call does is <see cref="ProcedureCalls"/>'. The guide's chapter <c>docs/internals/06-flow-joins-and-fixed-points.md</c>
/// explains the walk, and <c>docs/internals/04-abstract-interpretation.md</c> its division from the typer.
/// </summary>
/// <remarks>
/// The walk's own state is the paths it is following: the first statement that blocks it, the states at each RETURN, those
/// that reach each CATCH or may leave the body, and how many result sets were placed. It lives for one walk of one body.
/// </remarks>
internal sealed partial class Walker
{
    private readonly DatabaseModel model;
    private readonly Binder binder;
    private readonly SourceSpan source;

    /// <summary>What each expression and query of the body gives, read in the state a statement starts from.</summary>
    private readonly Typer typer;

    /// <param name="model">The model the body's references resolve in.</param>
    /// <param name="source">Where the object whose body it is was read; positions in the body are in its file.</param>
    /// <param name="summaries">What each object the body calls or reads does or holds.</param>
    /// <param name="parameters">Inside a function, its parameters' values.</param>
    /// <param name="context">
    /// What a procedure's walk starts from: its caller's temporary tables, and how it is called. Null inside a function, which
    /// follows no call of a procedure and fires no trigger.
    /// </param>
    /// <param name="ansiNullsOff">Whether the body was created with ANSI_NULLS off.</param>
    /// <param name="uncommittedReads">
    /// Whether the body (a function's) is read uncommitted where it is used, so that no key of what it reads holds.
    /// </param>
    public Walker(
        DatabaseModel model,
        SourceSpan source,
        Summaries summaries,
        EquatableMap<LocalName, Value>? parameters = null,
        CallContext? context = null,
        bool ansiNullsOff = false,
        bool uncommittedReads = false)
    {
        this.model = model;
        binder = summaries.Bindings.For(source);
        this.source = source;
        typer = new Typer(model, source, summaries, parameters, ansiNullsOff, uncommittedReads);
        this.summaries = summaries;
        followsCalls = context is not null;
        this.context = context ?? CallContext.Top;
        escapes = this.context.Escapes ? [] : null;
    }

    /// <summary>
    /// Session options known not to change which result sets run or their shape. Anything else is not modelled:
    /// NOEXEC, FMTONLY and PARSEONLY stop the rows from being returned at all, and turning off ANSI_WARNINGS
    /// or CONCAT_NULL_YIELDS_NULL changes where expressions produce null. Those that can only claim less if passed over are
    /// passed over (decision 0038, "A SET in a body"): ANSI_WARNINGS or ARITHABORT on makes an overflow an error, and
    /// NUMERIC_ROUNDABORT off makes a loss of precision silent; NUMERIC_ROUNDABORT on makes it NULL, past any declaration.
    /// </summary>
    private const SetOptions AnyValue = SetOptions.NoCount | SetOptions.XactAbort;

    private const SetOptions OnlyOn =
        SetOptions.AnsiNulls | SetOptions.AnsiPadding | SetOptions.AnsiWarnings | SetOptions.QuotedIdentifier | SetOptions.ArithAbort;

    private const SetOptions OnlyOff = SetOptions.NumericRoundAbort;

    private static bool IsHarmless(PredicateSetStatement set) =>
        (set.Options & ~(set.IsOn ? AnyValue | OnlyOn : AnyValue | OnlyOff)) == SetOptions.None;

    /// <summary>What a variable reference means in this body: a parameter, a variable it declares, or neither (<see cref="Binder.Resolve(VariableReference, NameEnvironment)"/>).</summary>
    private VariableResolution Resolved(VariableReference variable) => Binder.Resolve(variable, names);

    /// <summary>
    /// The names the body defines that hold for the whole of it: its table variables, as declared. The temporary tables'
    /// columns at a statement are added to them there (<see cref="Reading"/>).
    /// </summary>
    private NameEnvironment names = NameEnvironment.Empty;

    /// <summary>The first statement that makes the results impossible to list, once found.</summary>
    private UnknownShape? blocker;

    /// <summary>The procedure's statements, while it is walked.</summary>
    private StatementList? body;

    /// <summary>For each TRY being walked, innermost on top, every state an error inside it can reach its CATCH with.</summary>
    private readonly Stack<List<FlowState>> catchEntries = new();

    /// <summary>The states at each RETURN: paths that end the procedure, successfully, before its last statement.</summary>
    private readonly List<FlowState> returns = [];

    /// <summary>How many result sets have been added to any state, so that a loop can tell whether its body returns any.</summary>
    private int appended;

    /// <summary>Each scalar variable's declared type, from every DECLARE in the body.</summary>
    private readonly Dictionary<LocalName, Shape> variableShapes = [];

    /// <summary>
    /// What a statement leads to, the statement semantics' answer for it: the state where control goes next, and the states
    /// that reach a <c>BREAK</c> or a <c>CONTINUE</c>, which the loop around it joins in at its exit and its head. Each is null
    /// when no path goes there (the bottom, which a join leaves the other side of unchanged).
    /// </summary>
    /// <param name="Next">The state after the statement, for the statement that follows.</param>
    /// <param name="Breaks">The state that reaches a <c>BREAK</c> inside it.</param>
    /// <param name="Continues">The state that reaches a <c>CONTINUE</c> inside it.</param>
    private sealed record Outcome(FlowState? Next, FlowState? Breaks, FlowState? Continues)
    {
        /// <summary>A statement no path leaves: a <c>RETURN</c>, a <c>THROW</c>, or one that stops the walk.</summary>
        public static Outcome Ends { get; } = new(null, null, null);
    }

    /// <summary>
    /// Walks a procedure's body from <see cref="context"/>: the state at its end, joined over the paths that return
    /// successfully, and what stopped the walk, if anything did.
    /// </summary>
    /// <param name="parameters">The procedure's or function's parameters.</param>
    /// <param name="statements">Its body.</param>
    /// <param name="returnTable">
    /// A multi-statement function's return table, a table variable its body fills; null for a procedure.
    /// </param>
    public Walked Walk(IReadOnlyList<Parameter> parameters, StatementList statements, DeclaredTable? returnTable = null)
    {
        // Declarations are compiled, not run, so binding reads them from the whole body first (BodyDeclarations).
        var declarations = binder.Declarations(parameters, statements, returnTable);
        var start = new List<KeyValuePair<LocalName, Value>>();
        foreach (var parameter in declarations.Parameters)
        {
            var name = new LocalName(parameter.Name);
            variableShapes[name] = typer.DeclaredShape(parameter.Type, parameter.At, name, out var live);
            start.Add(new(name, Value.Computed(variableShapes[name], Because(IrNullRule.Parameter, name, variableShapes[name]), live)));
        }

        body = statements;
        foreach (var variable in declarations.Variables)
        {
            var name = variable.Name;
            variableShapes[name] = typer.DeclaredShape(variable.Type, variable.At, name, out var live);

            // A variable holds NULL until it is assigned.
            start.Add(new(name, Value.Computed(variableShapes[name], Because(IrNullRule.Variable, name, variableShapes[name]), live)));
        }

        var tableVariables = declarations.TableVariables.ToDictionary(t => new LocalName(t.Name), typer.TableVariableValues);
        names = NameEnvironment.Empty.WithDeclarations(declarations);
        var initial = FlowState.Start(start, declarations.Variables.Select(v => v.Name)).WithTemps(context.Temps).WithReadUncommitted(context.ReadUncommitted);

        // What each variable and table variable column of nvarchar(max) holds as JSON: nothing yet. A parameter is
        // the caller's, so it is not traced.
        foreach (var variable in declarations.Variables)
        {
            if (Traced(variableShapes[variable.Name]) && !initial.Json.ContainsKey(new VariableSlot(variable.Name)))
                initial = initial.WithJson(new VariableSlot(variable.Name), JsonSlot.Empty);
        }

        foreach (var (name, table) in tableVariables)
        {
            if (table is not { Names: { } columns, Values: { } values })
                continue;
            foreach (var (column, value) in columns.Zip(values))
            {
                if (Traced(value))
                    initial = initial.WithJson(new ColumnSlot(name, column), JsonSlot.Empty);
            }
        }

        var walked = Walk(statements.Statements, initial);
        if (blocker is not null)
            return new Walked(null, [], blocker, declaredUsed);

        // Paths that throw end in an error, which the IR does not describe: it describes calls that succeed.
        var end = FlowState.Join(returns.Prepend(walked.Next), source.Start);

        // An error after the last statement's results were sent leaves with all of them.
        if (end is not null)
            escapes?.Add(end);
        return new Walked(end, escapes is null ? [] : [.. escapes], null, declaredUsed);
    }

    // ---- statements ---------------------------------------------------------------------------------------------

    /// <summary>Walks a statement list from <paramref name="state"/>; null for a state no path reaches.</summary>
    private Outcome Walk(IEnumerable<TSqlStatement> statements, FlowState? state)
    {
        FlowState? breaks = null;
        FlowState? continues = null;
        foreach (var statement in statements)
        {
            if (state is null || blocker is not null)
                break;

            // A call or TRUNCATE may change a permanent table's rows, which ends what every existence guard found (FoundRow): the
            // row may be gone, by the body's own doing rather than another session's. A write does the same in Modify.
            if (MayWrite(statement))
                state = state.Written();

            // Every statement resets @@ROWCOUNT but the IF that tests it, which must come straight after the SELECT it counts
            // (observed: Any_statement_between_resets_rowcount_so_the_test_proves_nothing).
            if (statement is not IfStatement)
                state = state.Uncounted();

            // Any statement inside TRY may fail before it changes anything (a statement's effects are rolled back with it),
            // and its CATCH then starts from the state before it. Any statement at all may fail with an error that leaves the
            // procedure, to a CATCH of a caller's.
            if (catchEntries.Count > 0)
                catchEntries.Peek().Add(state);
            escapes?.Add(state);

            var outcome = Statement(statement, state);
            breaks = FlowState.Join(breaks, outcome.Breaks, At(statement));
            continues = FlowState.Join(continues, outcome.Continues, At(statement));
            state = outcome.Next;
        }

        return blocker is null ? new Outcome(state, breaks, continues) : Outcome.Ends;
    }

    private Outcome Statement(TSqlStatement statement, FlowState state)
    {
        var at = At(statement);
        switch (statement)
        {
            case PredicateSetStatement harmless when IsHarmless(harmless):
                return Continue(state);

            // The isolation level changes no result's shape, only whether a key holds (Cardinality.cs); it lasts until the
            // procedure returns (observed: A_procedures_isolation_level_holds_in_the_procedures_it_calls_and_ends_when_it_returns).
            case SetTransactionIsolationLevelStatement isolation:
                return Continue(state.WithReadUncommitted(isolation.Level == IsolationLevel.ReadUncommitted).WithIsolation(isolation.Level));
            case PrintStatement:
            case DeclareTableVariableStatement:
            // A transaction changes what a row an existence guard found is held by (FoundRow), and a rollback undoes what the
            // procedure did to its temporary tables.
            case BeginTransactionStatement:
                return Continue(state.Begun());
            case CommitTransactionStatement:
                return Continue(state.Committed());
            case SaveTransactionStatement:
                return Continue(state);
            case RollbackTransactionStatement:
                return Continue(RolledBack(state, at).Written(rolledBack: true));
            case DeclareVariableStatement declare:
                foreach (var element in declare.Declarations.Where(d => d.Value is not null))
                    state = Assign(state, names.Declarations.Declared(element).Name, typer.Expression(element.Value, Reading(state)), definitely: true, at, element.Value);
                return Continue(state);
            case SetVariableStatement set when set.CursorDefinition is null && set.Identifier is null && set.Expression is not null:
                return Continue(Assign(state, Resolved(set.Variable).Name, typer.AssignedValue(set.AssignmentKind, set.Expression, Reading(state), set), definitely: true, at, set.Expression));
            case BeginEndBlockStatement block:
                return Walk(block.StatementList.Statements, state);
            case IfStatement branch:
            {
                // Each arm starts knowing what its condition proves: a guard whose arm ends leaves the rest of the body the other's.
                // The IF's own test resets @@ROWCOUNT, so nothing inside it is counted.
                var otherwise = Narrowed(state, branch.Predicate, held: false).Uncounted();
                return Join(
                    Walk([branch.ThenStatement], Narrowed(state, branch.Predicate, held: true).Uncounted()),
                    branch.ElseStatement is null ? Continue(otherwise) : Walk([branch.ElseStatement], otherwise),
                    at);
            }

            case WhileStatement loop:
                return While(loop, state);
            case BreakStatement:
                return new Outcome(null, state, null);
            case ContinueStatement:
                return new Outcome(null, null, state);
            case ReturnStatement:
                returns.Add(state);
                return Outcome.Ends;

            // An error ends the call, or reaches CATCH from the state before it (recorded by Walk). Observed: THROW ends the
            // procedure; RAISERROR of severity 11 or more goes on but the call fails; 10 and below is a message.
            case ThrowStatement:
                return Outcome.Ends;
            case RaiseErrorStatement raise:
                return raise.SecondParameter is IntegerLiteral severity && int.TryParse(severity.Value, out var level) && level >= 11
                    ? Outcome.Ends
                    : Continue(state);
            case TryCatchStatement attempt:
                return Try(attempt, state);

            // Under INSERT … EXEC a FOR JSON query fails when it runs (13602), and so does another INSERT … EXEC (8164), both
            // observed: the path ends there.
            case SelectStatement { Into: null } forJson when context.Inserting && ForOf(forJson.QueryExpression) is JsonForClause:
                return Outcome.Ends;
            case InsertStatement { InsertSpecification.InsertSource: ExecuteInsertSource } when context.Inserting:
                return Outcome.Ends;
            case SelectStatement select:
                return Continue(Select(select, state));
            // INSERT … SELECT … FROM (DELETE … OUTPUT …) AS d: the nested write fires its own table's triggers, which may return
            // result sets (observed: A_write_nested_in_an_insert_fires_its_tables_triggers). Only the outer write is walked.
            case InsertStatement nesting when NestsAWrite(nesting):
                blocker ??= new UnknownShape(
                    IrUnknownReason.UnsupportedStatement,
                    "An INSERT whose rows come from a write nested in it is not modelled yet, so the procedure's result sets cannot be listed.",
                    At(nesting));
                return Outcome.Ends;
            case InsertStatement { InsertSpecification.InsertSource: ExecuteInsertSource } insert:
                return InsertExecute(insert, state);
            case InsertStatement insert:
                return Modify(insert, insert.InsertSpecification, null, null, insert.WithCtesAndXmlNamespaces, DmlEvents.Insert, state, target => Insert(insert, target, state));
            case ExecuteStatement execute:
                return Execute(execute, state);
            case UpdateStatement update:
                return Modify(update, update.UpdateSpecification, update.UpdateSpecification.FromClause, update.UpdateSpecification.WhereClause?.SearchCondition, update.WithCtesAndXmlNamespaces, DmlEvents.Update, state, target => Update(update, target, state));
            case DeleteStatement delete:
                return Modify(delete, delete.DeleteSpecification, delete.DeleteSpecification.FromClause, delete.DeleteSpecification.WhereClause?.SearchCondition, delete.WithCtesAndXmlNamespaces, DmlEvents.Delete, state, _ => state);
            case MergeStatement merge:
                return Modify(merge, merge.MergeSpecification, null, null, merge.WithCtesAndXmlNamespaces, DmlTriggers.MergeEvents(merge.MergeSpecification), state, target => Merge(merge, target, state));

            // TRUNCATE fires no trigger (observed: Truncate_does_not_fire_a_delete_trigger) and returns nothing, and removing
            // rows changes nothing a column may hold.
            case TruncateTableStatement:
                return Continue(state);

            // Cursors (observed: Cursor_statements_and_fetch_into_return_nothing_and_fetch_without_into_returns_one_row). A
            // cursor variable's query (SET @c = CURSOR …) is not followed: a FETCH from it reads values not known.
            case DeclareCursorStatement:
            case SetVariableStatement { CursorDefinition: not null }:
            case OpenCursorStatement:
            case CloseCursorStatement:
            case DeallocateCursorStatement:
                return Continue(state);
            case FetchCursorStatement fetch:
                return Fetch(fetch, state);
            case CreateTableStatement create when Binder.Temporary(create.SchemaObjectName) is { } created:
                return Continue(state.WithTemp(created, Created(create, created)));
            case DropTableStatement drop when drop.Objects.Select(Binder.Temporary).ToList() is var dropped && dropped.All(n => n is not null):
                foreach (var name in dropped.OfType<LocalName>())
                    state = state.WithTemp(name, new AbsentTable(at));
                return Continue(state);
            case AlterTableStatement alter when Binder.Temporary(alter.SchemaObjectName) is { } altered:
                return Continue(Alter(alter, altered, state));
            case CreateIndexStatement index when Binder.Temporary(index.OnName) is not null:
                return Continue(state);
            default:
                return Blocked(statement);
        }
    }

    private static Outcome Continue(FlowState state) => new(state, null, null);

    private Outcome Blocked(TSqlStatement statement)
    {
        var keyword = statement.ScriptTokenStream[statement.FirstTokenIndex].Text.ToUpperInvariant();
        blocker ??= new UnknownShape(
            IrUnknownReason.UnsupportedStatement,
            $"{keyword} is not modelled yet, so the procedure's result sets cannot be listed.",
            At(statement));
        return Outcome.Ends;
    }

    private static Outcome Join(Outcome a, Outcome b, SourceLocation at) => new(
        FlowState.Join(a.Next, b.Next, at),
        FlowState.Join(a.Breaks, b.Breaks, at),
        FlowState.Join(a.Continues, b.Continues, at));

    /// <summary>
    /// A loop: its body walked from the state at its head until another pass changes nothing, the head joining the state
    /// before the loop with the end of each pass and each CONTINUE. It ends at the head (its condition false) or at a BREAK.
    /// </summary>
    private Outcome While(WhileStatement loop, FlowState state)
    {
        var at = At(loop);
        var before = appended;
        var body = Outcome.Ends;
        var iteration = FixedPoint.Iterate(
            state,
            head =>
            {
                body = Walk([loop.Statement], head);
                if (blocker is not null)
                    return Pass<FlowState>.Stop();

                // How many times the body runs is not known, so neither is how many result sets it returns.
                if (appended != before)
                {
                    blocker = new UnknownShape(IrUnknownReason.VaryingResults, "A result set is returned inside a loop, which may return it any number of times.", at);
                    return Pass<FlowState>.Stop();
                }

                return Pass<FlowState>.To(FlowState.Join(FlowState.Join(state, body.Next, at), body.Continues, at));
            },

            // JSON built from itself grows at every pass (SET @x = (SELECT json_query(@x) AS c FOR JSON PATH)), so after a few its
            // content is widened to unknown, naming the loop, and the loop settles.
            widen: (head, next) => Widened(head, next, at));

        switch (iteration.End)
        {
            case IterationEnd.Settled:
                return new Outcome(FlowState.Join(iteration.Last, body.Breaks, at), null, null);
            case IterationEnd.Stopped:
                return Outcome.Ends;
            default:
                blocker = new UnknownShape(IrUnknownReason.UnsupportedStatement, $"What the loop does to the procedure's tables and variables reached no fixed point in {FixedPoint.MostPasses} passes.", at);
                return Outcome.Ends;
        }
    }

    /// <summary>
    /// TRY … CATCH. An error at any point inside TRY reaches CATCH: before any statement, or after the last, since a result
    /// set cut short by an error still arrives with the rows sent before it (observed). An error that ends the call instead
    /// (one TRY does not catch, such as a missing table, observed) is not a call the IR describes.
    /// </summary>
    private Outcome Try(TryCatchStatement attempt, FlowState state)
    {
        var at = At(attempt);
        var entries = new List<FlowState>();
        catchEntries.Push(entries);
        Outcome tried;
        try
        {
            tried = Walk(attempt.TryStatements.Statements, state);
            if (tried.Next is not null)
                entries.Add(tried.Next);
        }
        finally
        {
            catchEntries.Pop();
        }

        var caught = FlowState.Join(entries, at) is { } entry ? Walk(attempt.CatchStatements.Statements, entry.Uncounted().Caught()) : Outcome.Ends;
        return Join(tried, caught, at);
    }

    /// <summary>
    /// A rollback undoes what was done to temporary tables inside the transaction (observed), and the transaction may have
    /// been begun by the caller, so nothing the procedure did to one is known any more. Table variables and variables are
    /// not transactional.
    /// </summary>
    private static FlowState RolledBack(FlowState state, SourceLocation at)
    {
        var why = new UnknownTable(new UnknownShape(IrUnknownReason.UnsupportedStatement, $"The ROLLBACK at {at} may have undone what the procedure did to it.", at));
        return state.WithTemps(state.Temps.ToEquatableMap(t => t.Key, _ => (TempTable)why));
    }

    // ---- variables ------------------------------------------------------------------------------------------------

    /// <summary>
    /// The scope of a statement that starts from <paramref name="state"/>, outside any query: the body's names, with each
    /// temporary table's columns as the state knows them, for <c>SET @v = …</c>, <c>VALUES (…)</c> and every query the
    /// statement reads. The names change only where a temporary table's columns do, so a statement read again from a state
    /// with the same columns is bound again with the same names, which binding keeps (<see cref="Bindings"/>).
    /// </summary>
    private ReadScope Reading(FlowState state) => ReadScope.Statement(
        names.WithTemporaryColumns(state.Temps
            .Where(t => t.Value is KnownTable)
            .Select(t => KeyValuePair.Create(t.Key, (IReadOnlyList<string>)[.. ((KnownTable)t.Value).Columns.Select(c => c.Name)]))),
        state);

    /// <summary>
    /// Assigns a variable: its type stays its declared one, which the value is converted to, and it is null when the value
    /// may be. An assignment that may not happen (a SELECT that may match no rows) joins with the value before.
    /// </summary>
    /// <param name="expression">The expression assigned, when there is one: a JSON literal is read for its content.</param>
    private FlowState Assign(FlowState state, LocalName name, Value value, bool definitely, SourceLocation at, ScalarExpression? expression = null)
    {
        if (!variableShapes.TryGetValue(name, out var shape) || !state.Variables.TryGetValue(name, out var old))
            return state;
        state = state.Assigned(name);
        var because = shape is UnknownShape unknown ? NullReasons.Join(value.NullableBecause, [new IrNullReason(IrNullRule.Unknown, unknown.At.ToString())]) : Converting(value, shape, at, summaries.Session, expression);
        var assigned = Value.Computed(shape, because, old.IsLiveJson);
        state = state.WithVariable(name, definitely ? assigned : Value.Join(old, assigned, at, name.Text));
        if (state.Json.TryGetValue(new VariableSlot(name), out var slot))
        {
            var written = JsonWritten(value, expression, at, name.Text);
            state = state.WithJson(new VariableSlot(name), definitely ? written : JsonSlot.Join(slot, written));
        }

        return state;
    }

    // ---- SELECT ---------------------------------------------------------------------------------------------------

    private FlowState Select(SelectStatement select, FlowState state)
    {
        if (select.Into is not null)
            return SelectInto(select, state);

        if (select.QueryExpression is QuerySpecification { SelectElements: var elements } query && elements.All(e => e is SelectSetVariable))
            return typer.WithCtes(select.WithCtesAndXmlNamespaces, Reading(state), scope => SetVariables(query, state, scope), _ => SetVariables(query, state, Reading(state), unknown: true));

        var result = typer.Result(select, Reading(state))!; // Never null: a SELECT that returns none (INTO, assignments) is read above.
        appended++;
        return state.WithResults(state.Results.Append(result, At(select)));
    }

    /// <summary>
    /// <c>SELECT @a = …, @b = …</c>. A query that always returns exactly one row assigns exactly once: one without FROM or a
    /// filter, an aggregate without GROUP BY or HAVING, or one over an inline function whose body is one (<see cref="Cardinality"/>;
    /// observed: A_select_assigning_from_an_aggregate_without_group_by_assigns_even_over_no_rows,
    /// A_select_assigning_from_an_inline_function_whose_body_is_one_row_assigns_even_over_no_rows). Any other may assign once
    /// per row or not at all (observed: a SELECT that matches nothing leaves the variable as it was). Whether the row is there
    /// is apart from whether its value is null: SUM over no rows is assigned, and is NULL.
    /// </summary>
    /// <param name="query">The SELECT's query.</param>
    /// <param name="state">The state the assignments change.</param>
    /// <param name="at">The statement's scope, its WITH's CTEs included.</param>
    /// <param name="unknown">Whether its WITH is not modelled, so every value is unknown.</param>
    private FlowState SetVariables(QuerySpecification query, FlowState state, ReadScope at, bool unknown = false)
    {
        var entered = typer.Enter(query, at);
        var once = !unknown && typer.Rows(query, entered) == IrCardinality.One;
        var scope = entered.Assigning(query);
        var before = state;
        var read = new List<KeyValuePair<LocalName, Value>>();
        foreach (var assignment in query.SelectElements.Cast<SelectSetVariable>())
        {
            var value = unknown
                ? Value.Unknown(Unsupported(IrUnknownReason.UnsupportedQuery, "WITH XMLNAMESPACES is not modelled yet.", At(assignment)))
                : typer.AssignedValue(assignment.AssignmentKind, assignment.Expression, scope, assignment);
            var name = Resolved(assignment.Variable).Name;
            var expression = unknown ? null : assignment.Expression;

            // What the variable holds if the SELECT reads a row, kept for a guard that proves it did.
            if (!once && Assign(state, name, value, definitely: true, At(assignment), expression).Variables.TryGetValue(name, out var assigned))
                read.Add(new(name, assigned));
            state = Assign(state, name, value, once, At(assignment), expression);
        }

        return once ? state : Remembered(before, state, read);
    }

    // ---- the results ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The result sets of the paths that succeed. The i-th a client receives is the i-th of its path, so each position is
    /// the union of what any path returns there, and may be missing (<c>not-run</c>) when some path returns fewer.
    /// </summary>
    public static (List<ResultShape>? Results, UnknownShape? Varying) Results(ResultTrack track)
    {
        var count = track.Counts.Max;
        var always = track.Counts.Min;
        var results = new List<ResultShape>();
        for (var i = 0; i < count; i++)
        {
            var produced = track.Positions[i];
            if (produced.Select(p => p.Result.Encoding).Distinct().Count() > 1)
                return (null, new UnknownShape(IrUnknownReason.VaryingResults, $"Result set {i + 1} is a rowset on some paths and JSON on others.", produced[0].At));

            var shapes = new List<Shape>();
            foreach (var shape in produced.Select(p => p.Result.Shape))
            {
                if (!shapes.Contains(shape))
                    shapes.Add(shape);
            }

            var encoding = produced[0].Result.Encoding;
            // Absent if any statement here is, and, past the positions every path reaches, because a statement may not run.
            var absent = produced.Select(p => p.Result.AbsentBecause).Aggregate(NullReasons.None, NullReasons.Join);
            if (i >= always)
                absent = produced.Aggregate(absent, (all, p) => NullReasons.Join(all, [new IrNullReason(IrNullRule.NotRun, p.At.ToString())]));
            // The fewest rows any statement here proves at most: a statement that may return many makes the position many.
            var cardinality = produced.Select(p => p.Result.Cardinality).Aggregate(Cardinalities.Join);
            results.Add(new ResultShape(encoding, Union(shapes), absent, cardinality));
        }

        return (results, null);
    }

    /// <summary>
    /// The union of what several statements return at one position: one of their shapes whole, so a rowset is a union of
    /// arrays, every row of one result set from the same statement.
    /// </summary>
    private static Shape Union(List<Shape> shapes)
    {
        if (shapes.Count == 1)
            return shapes[0];
        return shapes.FirstOrDefault(s => s is UnknownShape) ?? new UnionShape([.. shapes]);
    }

    /// <summary>
    /// A multi-statement function's rows: its declared return table, whatever its body inserts. The table enforces its
    /// declaration (observed: A_multi_statement_functions_return_table_enforces_its_constraints): every value is converted to
    /// its column's type, a NULL into a NOT NULL column fails the call, and so does a row that breaks a PRIMARY KEY, a UNIQUE
    /// constraint or a CHECK. So its columns are the declared ones, with their CHECK facets, and its keys are its declared
    /// keys. How many rows it returns is not read.
    /// </summary>
    public LocalRelationValues ReadReturnTable(TableFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        var name = function.Name.ToString();
        var table = function.Returns;
        var values = table.Columns.Select(c => typer.DeclaredValue(table, c, name, temporary: false)).ToList();
        var keys = new List<RelationKey>();
        foreach (var key in SchemaRules.EnforcedKeys(table))
        {
            var ordinals = key.Columns.Select(c => table.Columns.ToList().FindIndex(x => x.Name.Equals(c, StringComparison.OrdinalIgnoreCase))).ToList();
            var types = key.Columns.Select(c => SchemaRules.ResolveType(model, table.Column(c)?.Type)?.Type).ToList();
            if (ordinals.All(o => o >= 0) && types.All(t => t is not null))
                keys.Add(new RelationKey([.. ordinals], [.. types.OfType<BuiltInType>()]));
        }

        // What its nvarchar(max) columns hold as JSON is what its body writes to them: the body is walked as a
        // procedure's is, the return table a table variable, and its traced columns read as they stand where it returns. A body
        // the walk cannot follow leaves them as declared.
        var walked = function.Body is SqlBody body ? Walk(function.Parameters, body.Statements, table) : null;
        if (walked is { Blocker: null, End: { } end })
        {
            var variable = new LocalName(table.Name);
            values = [.. values.Select((v, i) => end.Json.TryGetValue(new ColumnSlot(variable, table.Columns[i].Name), out var slot) ? slot.Read(v) : v)];
        }

        return new LocalRelationValues([.. table.Columns.Select(c => c.Name)], [.. values], null) { Keys = [.. keys] };
    }

    private SourceLocation At(TSqlFragment fragment) => source.At(fragment);
}
