using System.Collections.Immutable;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// The statement walk: a procedure's statements in order, carrying a FlowState from each to the next, with control flow
// joining the states of the paths that meet. README.md in this folder gives the model, the join rules and the
// observations they rest on (StatementFlowObservationTests in the differential tests).
internal sealed partial class ProcedureAnalyzer
{
    /// <summary>The body's relations and variables, while a procedure is walked; null inside a function.</summary>
    private FlowLocals? locals;

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
    private readonly Dictionary<string, Shape> variableShapes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Each parameter's and variable's declared type, for comparing it with a key (Cardinality.cs).</summary>
    private readonly Dictionary<string, SqlType?> variableTypes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The most passes a loop or a recursive CTE gets to reach a fixed point. Each joins only upwards, so a few are enough.</summary>
    private const int MostPasses = 20;

    private sealed record Outcome(FlowState? Next, FlowState? Breaks, FlowState? Continues)
    {
        public static Outcome Ends { get; } = new(null, null, null);
    }

    /// <summary>
    /// Walks a procedure's body from <see cref="context"/>: the state at its end, joined over the paths that return
    /// successfully, and what stopped the walk, if anything did.
    /// </summary>
    public Walked Walk(ProcedureStatementBody procedure)
    {
        // Declarations are compiled, not run (observed: a variable or table variable declared in a branch not taken still
        // exists), so they are collected from the whole body first. A name can be declared once in a batch.
        var tableVariables = new Dictionary<string, LocalRelationValues>(StringComparer.OrdinalIgnoreCase);
        var start = new List<KeyValuePair<string, Value>>();
        foreach (var parameter in procedure.Parameters)
        {
            var name = parameter.VariableName.Value;
            variableTypes[name] = SqlType.From(parameter.DataType);
            variableShapes[name] = DeclaredShape(SqlType.From(parameter.DataType), At(parameter), name, out var live);
            start.Add(new(name, new Value(variableShapes[name], Because(IrNullRules.Parameter, name, variableShapes[name]), live)));
        }

        body = procedure.StatementList;
        var declarations = new DeclarationCollector();
        procedure.StatementList.Accept(declarations);
        foreach (var cursor in declarations.Cursors)
        {
            if (!cursors.TryGetValue(cursor.Name.Value, out var same))
                cursors[cursor.Name.Value] = same = [];
            same.Add(cursor);
        }

        foreach (var element in declarations.Variables)
        {
            var name = element.VariableName.Value;
            if (variableShapes.ContainsKey(name))
                continue;
            variableTypes[name] = SqlType.From(element.DataType);
            variableShapes[name] = DeclaredShape(SqlType.From(element.DataType), At(element), name, out var live);

            // A variable holds NULL until it is assigned.
            start.Add(new(name, new Value(variableShapes[name], Because(IrNullRules.Variable, name, variableShapes[name]), live)));
        }

        foreach (var table in declarations.Tables)
        {
            var name = table.VariableName.Value;
            if (tableVariables.ContainsKey(name))
                continue;
            var definition = Table.FromDefinition(new ObjectName("", name), table.Definition, file, At(table));
            tableVariables[name] = new LocalRelationValues(
                [.. definition.Columns.Select(c => c.Name)],
                [.. definition.Columns.Select(c => Declared(definition, c, name, temporary: false))],
                null);
        }

        locals = Locals(tableVariables);
        var walked = Walk(procedure.StatementList.Statements, FlowState.Start(start).WithTemps(context.Temps).WithReadUncommitted(context.ReadUncommitted));
        if (blocker is not null)
            return new Walked(null, [], blocker, declaredUsed);

        // Paths that throw end in an error, which the IR does not describe: it describes calls that succeed.
        var end = FlowState.Join(returns.Prepend(walked.Next), At(procedure));

        // An error after the last statement's results were sent leaves with all of them.
        if (end is not null)
            escapes?.Add(end);
        return new Walked(end, escapes ?? [], null, declaredUsed);
    }

    private static IReadOnlyList<IrNullReason> Because(string rule, string name, Shape shape) =>
        shape is UnknownShape unknown ? [new IrNullReason(rule, name), new IrNullReason(IrNullRules.Unknown, unknown.At.ToString())] : [new IrNullReason(rule, name)];

    /// <summary>A variable's or parameter's type: every value it holds has been converted to it, so the type's facets hold.</summary>
    private Shape DeclaredShape(SqlType? type, SourceLocation at, string name, out bool live)
    {
        // A json value nests as JSON (observed for variables and columns), and its shape is in the data.
        live = SchemaRules.ResolveType(model, type)?.BuiltIn.Name == "json";
        return live
            ? Unsupported(IrUnknownReasons.JsonShapeUnknown, $"{name} is a json variable: its shape is in the data.", at)
            : SqlTypes.ToConvertedShape(model, type, at);
    }

    /// <summary>Every DECLARE in a body, at any depth.</summary>
    private sealed class DeclarationCollector : TSqlFragmentVisitor
    {
        public List<DeclareVariableElement> Variables { get; } = [];

        public List<DeclareTableVariableBody> Tables { get; } = [];

        public List<DeclareCursorStatement> Cursors { get; } = [];

        public override void Visit(DeclareVariableElement node) => Variables.Add(node);

        public override void Visit(DeclareCursorStatement node) => Cursors.Add(node);

        public override void Visit(DeclareTableVariableBody node) => Tables.Add(node);
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
        locals!.State = state;
        var at = At(statement);
        switch (statement)
        {
            case PredicateSetStatement harmless when IsHarmless(harmless):
                return Continue(state);

            // The isolation level changes no result's shape, only whether a key holds (Cardinality.cs); it lasts until the
            // procedure returns (observed: A_procedures_isolation_level_holds_in_the_procedures_it_calls_and_ends_when_it_returns).
            case SetTransactionIsolationLevelStatement isolation:
                return Continue(state.WithReadUncommitted(isolation.Level == IsolationLevel.ReadUncommitted));
            case PrintStatement:
            case DeclareTableVariableStatement:
            // A transaction's start and end change nothing here; only a rollback undoes what the procedure did.
            case BeginTransactionStatement:
            case CommitTransactionStatement:
            case SaveTransactionStatement:
                return Continue(state);
            case RollbackTransactionStatement:
                return Continue(RolledBack(state, at));
            case DeclareVariableStatement declare:
                foreach (var element in declare.Declarations.Where(d => d.Value is not null))
                    state = Assign(state, element.VariableName.Value, Expression(element.Value, NoFrom()), definitely: true, at);
                return Continue(state);
            case SetVariableStatement set when set.CursorDefinition is null && set.Identifier is null && set.Expression is not null:
                return Continue(Assign(state, set.Variable.Name, Assigned(set.AssignmentKind, set.Expression, NoFrom(), set), definitely: true, at));
            case BeginEndBlockStatement block:
                return Walk(block.StatementList.Statements, state);
            case IfStatement branch:
                return Join(Walk([branch.ThenStatement], state), branch.ElseStatement is null ? Continue(state) : Walk([branch.ElseStatement], state), at);
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
            case SelectStatement { Into: null, QueryExpression.ForClause: JsonForClause } when context.Inserting:
                return Outcome.Ends;
            case InsertStatement { InsertSpecification.InsertSource: ExecuteInsertSource } when context.Inserting:
                return Outcome.Ends;
            case SelectStatement select:
                return Continue(Select(select, state));
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
            case CreateTableStatement create when IsTemporary(create.SchemaObjectName):
                return Continue(state.WithTemp(create.SchemaObjectName.BaseIdentifier.Value, Created(create)));
            case DropTableStatement drop when drop.Objects.All(IsTemporary):
                foreach (var name in drop.Objects)
                    state = state.WithTemp(name.BaseIdentifier.Value, new AbsentTable(at));
                return Continue(state);
            case AlterTableStatement alter when IsTemporary(alter.SchemaObjectName):
                return Continue(Alter(alter, state));
            case CreateIndexStatement index when IsTemporary(index.OnName):
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
            IrUnknownReasons.UnsupportedStatement,
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
        var head = state;
        for (var pass = 0; pass < MostPasses; pass++)
        {
            var body = Walk([loop.Statement], head);
            if (blocker is not null)
                return Outcome.Ends;

            // How many times the body runs is not known, so neither is how many result sets it returns.
            if (appended != before)
            {
                blocker = new UnknownShape(IrUnknownReasons.VaryingResults, "A result set is returned inside a loop, which may return it any number of times.", at);
                return Outcome.Ends;
            }

            var next = FlowState.Join([state, body.Next, body.Continues], at)!;
            if (next.Text() == head.Text())
                return new Outcome(FlowState.Join(head, body.Breaks, at), null, null);
            head = next;
        }

        blocker = new UnknownShape(IrUnknownReasons.UnsupportedStatement, $"What the loop does to the procedure's tables and variables reached no fixed point in {MostPasses} passes.", at);
        return Outcome.Ends;
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

        var caught = FlowState.Join(entries, at) is { } entry ? Walk(attempt.CatchStatements.Statements, entry) : Outcome.Ends;
        return Join(tried, caught, at);
    }

    /// <summary>
    /// A rollback undoes what was done to temporary tables inside the transaction (observed), and the transaction may have
    /// been begun by the caller, so nothing the procedure did to one is known any more. Table variables and variables are
    /// not transactional.
    /// </summary>
    private static FlowState RolledBack(FlowState state, SourceLocation at)
    {
        var why = new UnknownTable(new UnknownShape(IrUnknownReasons.UnsupportedStatement, $"The ROLLBACK at {at} may have undone what the procedure did to it.", at));
        return state.WithTemps(state.Temps.ToImmutableDictionary(t => t.Key, _ => (TempTable)why, state.Temps.KeyComparer));
    }

    // ---- variables ------------------------------------------------------------------------------------------------

    /// <summary>A scope with no FROM, for an expression outside a query: <c>SET @v = …</c>, <c>VALUES (…)</c>.</summary>
    private BindingScope NoFrom() => binder.BindFrom(new QuerySpecification(), null, locals);

    private Value Variable(VariableReference variable)
    {
        if (!locals!.State.Variables.TryGetValue(variable.Name, out var value))
            return Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedExpression, $"The variable {variable.Name} is not declared.", At(variable)));

        // A cursor's query takes its variables as they were when its DECLARE ran (observed:
        // A_cursors_query_takes_its_variables_as_they_are_when_it_is_declared), not where the FETCH reads it: any value of
        // the variable's declared type, which every assignment converts to, or null.
        if (unfiltered && variableShapes.TryGetValue(variable.Name, out var declared))
            return new Value(declared, Because(IrNullRules.Variable, variable.Name, declared), value.IsLiveJson);
        return value;
    }

    /// <summary>The value an assignment writes: the expression, unless a compound one (<c>+=</c>) combines it with the old.</summary>
    private Value Assigned(AssignmentKind kind, ScalarExpression expression, BindingScope scope, TSqlFragment at) =>
        kind == AssignmentKind.Equals
            ? Expression(expression, scope)
            : Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedExpression, $"The compound assignment {Text(at)} is not modelled yet.", At(at)));

    /// <summary>
    /// Assigns a variable: its type stays its declared one, which the value is converted to, and it is null when the value
    /// may be. An assignment that may not happen (a SELECT that may match no rows) joins with the value before.
    /// </summary>
    private FlowState Assign(FlowState state, string name, Value value, bool definitely, SourceLocation at)
    {
        if (!variableShapes.TryGetValue(name, out var shape) || !state.Variables.TryGetValue(name, out var old))
            return state;
        var because = shape is UnknownShape unknown ? ShapeJoin.Reasons(value.NullableBecause, [new IrNullReason(IrNullRules.Unknown, unknown.At.ToString())]) : Converting(value, shape, at);
        var assigned = new Value(shape, because, old.IsLiveJson);
        return state.WithVariable(name, definitely ? assigned : ShapeJoin.Either(old, assigned, at, name));
    }

    // ---- SELECT ---------------------------------------------------------------------------------------------------

    private FlowState Select(SelectStatement select, FlowState state)
    {
        if (select.Into is not null)
            return SelectInto(select, state);

        if (select.QueryExpression is QuerySpecification { SelectElements: var elements } query && elements.All(e => e is SelectSetVariable))
            return WithCtes(select.WithCtesAndXmlNamespaces, () => SetVariables(query, state), _ => SetVariables(query, state, unknown: true));

        var result = Select(select)!;
        appended++;
        return state.WithResults(state.Results.Append(result, At(select)));
    }

    /// <summary>
    /// <c>SELECT @a = …, @b = …</c>. Without FROM, WHERE, GROUP BY, HAVING or TOP it assigns exactly once; otherwise it may
    /// assign once per row or not at all (observed: a SELECT that matches nothing leaves the variable as it was).
    /// </summary>
    private FlowState SetVariables(QuerySpecification query, FlowState state, bool unknown = false)
    {
        var once = query is { FromClause: null, WhereClause: null, GroupByClause: null, HavingClause: null, TopRowFilter: null };
        var scope = binder.BindFrom(query, null, locals);
        var outer = current;
        current = query;
        try
        {
            foreach (var assignment in query.SelectElements.Cast<SelectSetVariable>())
            {
                var value = unknown
                    ? Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedQuery, "WITH XMLNAMESPACES is not modelled yet.", At(assignment)))
                    : Assigned(assignment.AssignmentKind, assignment.Expression, scope, assignment);
                state = Assign(state, assignment.Variable.Name, value, once, At(assignment));
            }

            return state;
        }
        finally
        {
            current = outer;
        }
    }

    /// <summary>
    /// <c>SELECT … INTO #t</c>: a new table whose columns are the select's, each holding what the select gives, with the
    /// facts and origins of a value that passes through unchanged. JSON is text once stored (observed), unless its SQL type
    /// is json, which is not tracked, so live JSON stored here is unknown.
    /// </summary>
    private FlowState SelectInto(SelectStatement select, FlowState state)
    {
        var at = At(select);
        if (!IsTemporary(select.Into))
        {
            Blocked(select);
            return state;
        }

        var name = select.Into.BaseIdentifier.Value;
        var table = WithCtes<TempTable>(select.WithCtesAndXmlNamespaces, () =>
        {
            if (select.QueryExpression is not QuerySpecification query)
                return Unknown(name, Unsupported(IrUnknownReasons.UnsupportedQuery, "A UNION, INTERSECT, EXCEPT or parenthesised query is not modelled yet.", at));
            var row = Query(query, parent: null);
            if (row.Star is { } star)
                return Unknown(name, Unsupported(IrUnknownReasons.UnsupportedQuery, "SELECT * is not expanded yet.", star));
            if (row.Columns.Any(c => string.IsNullOrEmpty(c.Name)) || row.Columns.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                return Unknown(name, Unsupported(IrUnknownReasons.UnnamedColumn, $"SELECT … INTO {name} has a column without a name, or two of one name, which SQL Server refuses.", at));
            var identity = Identities(query);
            return new KnownTable(name, [.. row.Columns.Select((c, i) => new TempColumn(c.Name!, Stored(c.Value, $"{name}.{c.Name}", c.At), null, identity[i]))]);
        }, why => Unknown(name, why));
        return state.WithTemp(name, table);
    }

    private static UnknownTable Unknown(string name, UnknownShape why) => new(why with { Detail = $"{name}: {why.Detail}" });

    /// <summary>
    /// Whether each column <c>SELECT … INTO</c> makes is an identity column: SQL Server copies the IDENTITY property of a
    /// column read unchanged from one table (observed), which matters to an insert without a column list, which skips it.
    /// It is documented not to be copied through an expression, so a column that is not a plain column reference is not one.
    /// Through a join, a column listed twice, or a relation whose columns' properties are not known (a CTE, a table
    /// variable, a caller's table), it may be: null.
    /// </summary>
    private bool?[] Identities(QuerySpecification query)
    {
        var scope = binder.BindFrom(query, null, locals);
        var single = query.FromClause?.TableReferences is [NamedTableReference];
        var bound = query.SelectElements.OfType<SelectScalarExpression>()
            .Select(e => e.Expression is ColumnReferenceExpression column ? binder.BindColumn(column, scope) : null)
            .ToList();
        var elements = query.SelectElements.OfType<SelectScalarExpression>().ToList();
        return [.. elements.Select((element, i) =>
        {
            if (bound[i] is not { } binding)
                return element.Expression is ParenthesisExpression ? (bool?)null : false;
            bool? identity = binding switch
            {
                BoundColumn column => column.Column.IsIdentity,
                BoundLocalColumn { Relation: var relation, Ordinal: >= 0 and var ordinal } when locals!.IsTemporary(relation)
                    && locals.State.Temp(relation.Name) is KnownTable known => known.Columns[ordinal].Identity,
                _ => null,
            };

            // Listed twice, the property is documented not to be copied; claimed only of a column listed once.
            var once = bound.Count(b => Same(b, binding)) == 1;
            return identity == false ? false : single && once ? identity : null;
        })];

        static bool Same(ColumnBinding? a, ColumnBinding b) => (a, b) switch
        {
            (BoundColumn x, BoundColumn y) => ReferenceEquals(x.Column, y.Column),
            (BoundLocalColumn x, BoundLocalColumn y) => ReferenceEquals(x.Relation, y.Relation) && x.Ordinal == y.Ordinal,
            _ => ReferenceEquals(a, b),
        };
    }

    private static Value Stored(Value value, string column, SourceLocation at) => value.IsLiveJson
        ? Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedExpression, $"JSON stored in {column} is text unless its SQL type is json, which is not tracked yet.", at))
        : value;

    // ---- writes to temporary tables -------------------------------------------------------------------------------

    /// <summary>
    /// What an INSERT does to the temporary table it writes directly: a declared column is unchanged by it, and a column
    /// SELECT … INTO made takes each value converted to its type, and null when the insert leaves it out. A table variable
    /// is its declaration, and any other target holds no state here.
    /// </summary>
    private FlowState Insert(InsertStatement insert, DmlTarget target, FlowState state)
    {
        var spec = insert.InsertSpecification;
        if (target.Direct is not { } name || !name.StartsWith('#') || state.Temp(name) is not KnownTable table)
            return state;

        var at = At(insert);

        // DEFAULT VALUES writes no column: each gets its default, or null, and a column SELECT … INTO made has no default.
        var targets = spec.InsertSource is ValuesInsertSource { IsDefaultValues: true } ? []
            : spec.Columns.Count == 0
            ? [.. Enumerable.Range(0, table.Columns.Count)]
            : spec.Columns.Select(c => IndexOf(table, c.MultiPartIdentifier.Identifiers[^1].Value)).ToList();
        if (targets.Any(i => i < 0))
            return state.WithTemp(name, Unknown(name, Unsupported(IrUnknownReasons.UnresolvedColumn, "The insert names a column the table does not have.", at)));

        var rows = WithCtes(insert.WithCtesAndXmlNamespaces, () => InsertedRows(spec.InsertSource, targets.Count, at), why => [[.. targets.Select(_ => Value.Unknown(why))]]);
        if (rows is null)
            return state.WithTemp(name, Unknown(name, Unsupported(IrUnknownReasons.UnsupportedStatement, "The insert's values do not match its columns one to one.", at)));

        return state.WithTemp(name, Written(table, targets, rows, at));
    }

    /// <summary>
    /// The table after rows are inserted into it: a declared column is unchanged (the engine converts each value to its type
    /// and refuses a null where it is NOT NULL); a column SELECT … INTO made joins with each value, and with null when the
    /// insert leaves it out, unless it is an identity column, which takes its next value.
    /// </summary>
    /// <param name="targets">The table's column each value of a row goes to, by position.</param>
    private static KnownTable Written(KnownTable table, List<int> targets, IEnumerable<IReadOnlyList<Value>> rows, SourceLocation at)
    {
        var columns = table.Columns.ToList();
        for (var i = 0; i < columns.Count; i++)
        {
            if (columns[i].Declaration is not null)
                continue;
            var column = columns[i];
            var where = $"{table.Name}.{column.Name}";
            var position = targets.IndexOf(i);

            // An identity column the insert leaves out gets its next value, never null.
            if (position < 0 && column.Identity == true)
                continue;
            var written = position < 0
                ? [new Value(column.Value.Shape, [new IrNullReason(IrNullRules.NullableColumn, where)])]
                : rows.Select(r => Stored(r[position], where, at));
            columns[i] = column with { Value = written.Aggregate(column.Value, (all, value) => ShapeJoin.Converted(all, value, at, where)) };
        }

        return table with { Columns = columns };
    }

    /// <summary>The values each row of an insert writes, by the insert's column positions; null when a row has the wrong number.</summary>
    private List<IReadOnlyList<Value>>? InsertedRows(InsertSource source, int count, SourceLocation at)
    {
        switch (source)
        {
            case ValuesInsertSource { IsDefaultValues: true }:
                return [];
            case ValuesInsertSource values:
                var rows = values.RowValues.Select(r => (IReadOnlyList<Value>)[.. r.ColumnValues.Select(v => Expression(v, NoFrom()))]).ToList();
                return rows.All(r => r.Count == count) ? rows : null;
            case SelectInsertSource { Select: QuerySpecification query }:
                var row = Query(query, parent: null);
                if (row.Star is { } star)
                    return [[.. Enumerable.Range(0, count).Select(_ => Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedQuery, "SELECT * is not expanded yet.", star)))]];
                return row.Columns.Count == count ? [[.. row.Columns.Select(c => c.Value)]] : null;
            default:
                return [[.. Enumerable.Range(0, count).Select(_ => Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedQuery, "This insert's source is not modelled yet.", at)))]];
        }
    }

    private static int IndexOf(KnownTable table, string column) =>
        table.Columns.ToList().FindIndex(c => c.Name.Equals(column, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What an UPDATE does: to the temporary table it writes directly, a column SELECT … INTO made joins with each value
    /// written to it, and a declared one is unchanged. A variable set by it, whatever its target, may not be (no rows), so
    /// it joins with its value before.
    /// </summary>
    private FlowState Update(UpdateStatement update, DmlTarget written, FlowState state)
    {
        var spec = update.UpdateSpecification;
        var at = At(update);
        var target = written.Direct ?? "";

        return WithCtes(update.WithCtesAndXmlNamespaces, () =>
        {
            var from = spec.FromClause ?? new FromClause { TableReferences = { spec.Target } };
            var query = new QuerySpecification { FromClause = from, WhereClause = spec.WhereClause };
            var scope = binder.BindFrom(query, null, locals);
            var table = target.StartsWith('#') ? state.Temp(target) as KnownTable : null;
            var columns = table?.Columns.ToList();
            foreach (var clause in spec.SetClauses)
            {
                if (clause is not AssignmentSetClause set)
                    return table is null ? state : state.WithTemp(target, Unknown(target, Unsupported(IrUnknownReasons.UnsupportedStatement, $"The update {Text(clause)} is not modelled yet.", At(clause))));

                var value = Assigned(set.AssignmentKind, set.NewValue, scope, set);
                if (set.Variable is { } variable)
                    state = Assign(state, variable.Name, value, definitely: false, At(set));
                if (set.Column is null || columns is null)
                    continue;

                var i = IndexOf(table!, set.Column.MultiPartIdentifier.Identifiers[^1].Value);
                if (i < 0)
                    return state.WithTemp(target, Unknown(target, Unsupported(IrUnknownReasons.UnresolvedColumn, $"The update sets {Text(set.Column)}, which {target} does not have.", At(set))));
                if (columns[i].Declaration is null)
                {
                    var where = $"{target}.{columns[i].Name}";
                    columns[i] = columns[i] with { Value = ShapeJoin.Converted(columns[i].Value, Stored(value, where, at), at, where) };
                }
            }

            return table is null ? state : state.WithTemp(target, table with { Columns = columns! });
        }, why => target.StartsWith('#') ? state.WithTemp(target, Unknown(target, why)) : state);
    }

    /// <summary>MERGE into a temporary table is not modelled, so the table becomes unknown; a table variable is as declared.</summary>
    private FlowState Merge(MergeStatement merge, DmlTarget target, FlowState state) =>
        target.Direct is { } name && name.StartsWith('#') && state.Temp(name) is KnownTable
            ? state.WithTemp(name, Unknown(name, Unsupported(IrUnknownReasons.UnsupportedStatement, $"MERGE at {At(merge)} is not modelled yet.", At(merge))))
            : state;

    // ---- creating and altering temporary tables -----------------------------------------------------------------

    /// <summary>
    /// Whether a name is a local temporary table's: one part, starting with one <c>#</c>. A global one (<c>##t</c>) is shared
    /// with every session, which may change it at any time, so it is treated as a permanent table.
    /// </summary>
    private static bool IsTemporary(SchemaObjectName name) =>
        name is { ServerIdentifier: null, DatabaseIdentifier: null, SchemaIdentifier: null }
        && name.BaseIdentifier.Value.StartsWith('#') && !name.BaseIdentifier.Value.StartsWith("##", StringComparison.Ordinal);

    /// <summary>
    /// <c>CREATE TABLE #t</c>. It always makes a new table: one of the caller's of the same name is hidden (observed). A
    /// procedure cannot create one name twice, even after dropping it (error 2714 when the procedure is created, observed),
    /// so re-creation never reaches a procedure that runs; it is modelled as the new table all the same.
    /// </summary>
    private KnownTable Created(CreateTableStatement create)
    {
        var name = create.SchemaObjectName.BaseIdentifier.Value;
        var definition = Table.FromDefinition(new ObjectName("", name), create.Definition, file, At(create));
        return new KnownTable(name, [.. definition.Columns.Select(c => new TempColumn(c.Name, Declared(definition, c, name, temporary: true), definition, c.IsIdentity))]);
    }

    /// <summary>
    /// What a declared column of a temporary table or table variable holds: its type, with the facts its CHECK constraints
    /// prove, and null unless its declaration (NOT NULL, the primary key, IDENTITY) rules it out.
    /// </summary>
    private Value Declared(Table table, Column column, string name, bool temporary)
    {
        var where = $"{name}.{column.Name}";
        IReadOnlyList<IrNullReason> because = SchemaRules.IsNeverNull(model, table, column) ? [] : [new IrNullReason(IrNullRules.NullableColumn, where)];

        // A #table is created in tempdb, which does not have the database's alias types (observed: 2715), so a name there
        // does not mean this project's type. sysname is the system's own, which tempdb has. A table variable takes them from
        // the database (observed).
        if (temporary && column.Type is { Kind: SqlTypeKind.Named } named
            && !(named.Name.Equals("sysname", StringComparison.OrdinalIgnoreCase) && named.Schema is null or "sys"))
            return Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedSqlType, $"{where} is of the alias type {named}, which tempdb has only if it was created there.", column.At));

        if (!column.IsComputed && SchemaRules.ResolveType(model, column.Type)?.BuiltIn.Name == "json")
            return new Value(Unsupported(IrUnknownReasons.JsonShapeUnknown, $"{where} is a json column: its shape is in the data.", column.At), because, IsLiveJson: true);

        var shape = ColumnShape(table, column);
        return shape is UnknownShape unknown ? Value.Unknown(unknown) : new Value(shape, because);
    }

    private FlowState Alter(AlterTableStatement alter, FlowState state)
    {
        var name = alter.SchemaObjectName.BaseIdentifier.Value;
        var at = At(alter);
        if (state.Temp(name) is not KnownTable table)
            return state;

        switch (alter)
        {
            case AlterTableAddTableElementStatement add:
            {
                // A new column's declaration is read like CREATE TABLE's. Constraints added on existing columns are not
                // read, which only claims less. WITH NOCHECK leaves the new columns' own checks unproved for the rows there.
                var definition = Table.FromDefinition(new ObjectName("", name), add.Definition, file, at);
                if (add.ExistingRowsCheckEnforcement == ConstraintEnforcement.NoCheck)
                    definition = Disabled(definition, null, at);
                return state.WithTemp(name, table with
                {
                    Columns = [.. table.Columns, .. definition.Columns.Select(c => new TempColumn(c.Name, Declared(definition, c, name, temporary: true), definition, c.IsIdentity))],
                });
            }

            case AlterTableAlterColumnStatement change when change.DataType is not null
                && change.AlterTableAlterColumnOption is AlterTableAlterColumnOption.NoOptionDefined or AlterTableAlterColumnOption.Null or AlterTableAlterColumnOption.NotNull:
            {
                // The column takes the new type; without NOT NULL it may be null. Its checks are left out, which claims less.
                var i = IndexOf(table, change.ColumnIdentifier.Value);
                if (i < 0)
                    break;
                var column = new Column(
                    table.Columns[i].Name,
                    SqlType.From(change.DataType),
                    change.AlterTableAlterColumnOption == AlterTableAlterColumnOption.NotNull ? DeclaredNullability.NotNull : DeclaredNullability.Null,
                    isIdentity: false,
                    computedExpression: null,
                    at,
                    change.Collation?.Value);
                var definition = new Table(new ObjectName("", name), [column], [], at);
                var columns = table.Columns.ToList();
                columns[i] = new TempColumn(column.Name, Declared(definition, column, name, temporary: true), definition, table.Columns[i].Identity);
                return state.WithTemp(name, table with { Columns = columns });
            }

            case AlterTableDropTableElementStatement drop:
            {
                var columns = table.Columns.ToList();
                foreach (var element in drop.AlterTableDropTableElements)
                {
                    switch (element.TableElementType)
                    {
                        case TableElementType.Column:
                            columns.RemoveAll(c => c.Name.Equals(element.Name.Value, StringComparison.OrdinalIgnoreCase));
                            break;
                        case TableElementType.Constraint or TableElementType.NotSpecified:
                            // A dropped CHECK proves nothing more. One of an unknown name may be a check without one.
                            columns = Rechecked(columns, element.Name.Value, name, at, drop: true);
                            break;
                        case TableElementType.Index:
                            break;
                        default:
                            return state.WithTemp(name, Unknown(name, Unsupported(IrUnknownReasons.UnsupportedStatement, $"{Text(alter)} is not modelled yet.", at)));
                    }
                }

                return state.WithTemp(name, table with { Columns = columns });
            }

            case AlterTableConstraintModificationStatement modify when modify.ConstraintEnforcement == ConstraintEnforcement.NoCheck:
            {
                // NOCHECK CONSTRAINT disables a check, which then checks no row (see CheckReader). CHECK CONSTRAINT enables
                // it again, but is not read, which only claims less.
                var columns = table.Columns.ToList();
                if (modify.All)
                    return state.WithTemp(name, table with { Columns = Rechecked(columns, null, name, at, drop: false) });
                foreach (var constraint in modify.ConstraintNames)
                    columns = Rechecked(columns, constraint.Value, name, at, drop: false);
                return state.WithTemp(name, table with { Columns = columns });
            }

            case AlterTableConstraintModificationStatement:
                return state;
        }

        return state.WithTemp(name, Unknown(name, Unsupported(IrUnknownReasons.UnsupportedStatement, $"{Text(alter)} is not modelled yet.", at)));
    }

    /// <summary>
    /// The columns again, each declared one read with the check named <paramref name="constraint"/> (every check, when null,
    /// or when no check has that name, since it may be one SQL Server named) disabled or dropped.
    /// </summary>
    private List<TempColumn> Rechecked(List<TempColumn> columns, string? constraint, string name, SourceLocation at, bool drop)
    {
        var named = constraint is not null && columns.Any(c => c.Declaration?.Checks.Any(k => constraint.Equals(k.Name, StringComparison.OrdinalIgnoreCase)) == true);
        return [.. columns.Select(c =>
        {
            if (c.Declaration is not { } declaration)
                return c;
            var changed = drop
                ? new Table(declaration.Name, declaration.Columns, declaration.PrimaryKey, declaration.At, [.. declaration.Checks.Where(k => !Matches(k))], declaration.UniqueKeys)
                : Disabled(declaration, k => Matches(k), at);
            return c with { Value = Declared(changed, changed.Column(c.Name)!, name, temporary: true), Declaration = changed };
        })];

        bool Matches(CheckConstraint check) => !named || constraint!.Equals(check.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static Table Disabled(Table table, Func<CheckConstraint, bool>? which, SourceLocation at) =>
        new(table.Name, table.Columns, table.PrimaryKey, table.At, [.. table.Checks.Select(k => which is null || which(k) ? k.WithDisabledAt(at) : k)]);

    // ---- CTEs -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Runs <paramref name="body"/> with the CTEs of <paramref name="with"/> visible, each defined in order so that it may
    /// read the ones before it. A CTE is part of its statement's query, so its values pass through unchanged, live JSON
    /// included (observed).
    /// </summary>
    private T WithCtes<T>(WithCtesAndXmlNamespaces? with, Func<T> body, Func<UnknownShape, T> failed)
    {
        if (with is null)
            return body();
        if (with.XmlNamespaces is not null)
            return failed(Unsupported(IrUnknownReasons.UnsupportedQuery, "WITH XMLNAMESPACES is not modelled yet.", At(with)));
        if (locals is null)
            return failed(Unsupported(IrUnknownReasons.UnsupportedQuery, "A common table expression (WITH) is not modelled here yet.", At(with)));

        var frame = locals.PushCtes();
        try
        {
            foreach (var cte in with.CommonTableExpressions)
                frame[cte.ExpressionName.Value] = locals.Register(cte.ExpressionName.Value, Cte(cte, frame));
            return body();
        }
        finally
        {
            locals.PopCtes();
        }
    }

    private LocalRelationValues Cte(CommonTableExpression cte, Dictionary<string, LocalRelation> frame)
    {
        var name = cte.ExpressionName.Value;
        var at = At(cte);
        LocalRelationValues Fails(string reason, string detail) => LocalRelationValues.Unknown(Unsupported(reason, $"{name}: {detail}", at));

        var members = new List<QueryExpression>();
        var operators = new List<BinaryQueryExpression>();
        Flatten(cte.QueryExpression, members, operators);
        var recursive = members.Select(m => References(m, name)).ToList();

        if (!recursive.Contains(true))
        {
            if (cte.QueryExpression is not QuerySpecification query)
                return Fails(IrUnknownReasons.UnsupportedQuery, "a CTE whose query is a UNION, INTERSECT, EXCEPT or parenthesised query is not modelled yet.");
            return Relation(cte, Query(query, parent: null)) ?? Fails(IrUnknownReasons.UnsupportedQuery, "its columns cannot be listed: SELECT * is not expanded yet, and every column needs one name of its own.");
        }

        // A recursive CTE is an anchor member, then recursive members, each joined by UNION ALL; SQL Server refuses any
        // other form. Several anchors (themselves a UNION) are not modelled yet.
        if (recursive[0] || recursive.Skip(1).Contains(false) || members.Any(m => m is not QuerySpecification)
            || operators.Any(o => o.BinaryQueryExpressionType != BinaryQueryExpressionType.Union || !o.All))
            return Fails(IrUnknownReasons.UnsupportedQuery, "only a recursive CTE of one anchor member and recursive members, joined by UNION ALL, is modelled.");

        // The column types are the anchor's exactly (SQL Server refuses a recursive member of any other, error 240,
        // observed), so only nullability, and the facets the members share, need a fixed point: the recursive members are
        // read with the CTE as it stands, joined in, and read again until nothing changes. Each pass only joins upwards.
        if (Relation(cte, Query((QuerySpecification)members[0], parent: null)) is not { } current)
            return Fails(IrUnknownReasons.UnsupportedQuery, "its anchor's columns cannot be listed: SELECT * is not expanded yet, and every column needs one name of its own.");
        for (var pass = 0; pass < MostPasses; pass++)
        {
            frame[name] = locals!.Register(name, current);
            var next = current.Values!.ToList();
            foreach (var member in members.Skip(1).Cast<QuerySpecification>())
            {
                var row = Query(member, parent: null);
                if (row.Star is not null || row.Columns.Count != next.Count)
                    return Fails(IrUnknownReasons.UnsupportedQuery, "a recursive member's columns cannot be listed against the anchor's.");
                for (var i = 0; i < next.Count; i++)
                    next[i] = ShapeJoin.Converted(next[i], row.Columns[i].Value, row.Columns[i].At, $"{name}.{current.Names![i]}");
            }

            if (next.Select(ShapeText.Of).SequenceEqual(current.Values!.Select(ShapeText.Of), StringComparer.Ordinal))
                return current;
            current = current with { Values = next };
        }

        return Fails(IrUnknownReasons.UnresolvedRecursion, $"its columns reached no fixed point in {MostPasses} passes.");
    }

    /// <summary>A CTE's columns from its query's row, named by its column list when it has one; null when they cannot be listed.</summary>
    private static LocalRelationValues? Relation(CommonTableExpression cte, Row row) => Relation(cte.Columns, row);

    /// <summary>A CTE's or derived table's columns from its query's row, named by its column list when it has one.</summary>
    private static LocalRelationValues? Relation(IList<Identifier> columns, Row row)
    {
        if (row.Star is not null)
            return null;
        List<string?> names = columns.Count > 0 ? [.. columns.Select(c => c.Value)] : [.. row.Columns.Select(c => c.Name)];
        if (names.Count != row.Columns.Count || names.Any(string.IsNullOrEmpty) || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
            return null;
        return new LocalRelationValues([.. names.Select(n => n!)], [.. row.Columns.Select(c => c.Value)], null);
    }

    private static void Flatten(QueryExpression query, List<QueryExpression> members, List<BinaryQueryExpression> operators)
    {
        if (query is BinaryQueryExpression binary)
        {
            Flatten(binary.FirstQueryExpression, members, operators);
            operators.Add(binary);
            Flatten(binary.SecondQueryExpression, members, operators);
        }
        else
        {
            members.Add(query);
        }
    }

    /// <summary>Whether a query reads a table of this one-part name: in a CTE, the CTE itself.</summary>
    private static bool References(QueryExpression query, string name)
    {
        var finder = new NameFinder(name);
        query.Accept(finder);
        return finder.Found;
    }

    private sealed class NameFinder(string name) : TSqlFragmentVisitor
    {
        public bool Found { get; private set; }

        public override void Visit(NamedTableReference node) =>
            Found |= node.SchemaObject is { SchemaIdentifier: null, DatabaseIdentifier: null, ServerIdentifier: null }
                && node.SchemaObject.BaseIdentifier.Value.Equals(name, StringComparison.OrdinalIgnoreCase);
    }

    // ---- reading the body's own relations and expressions it needs ----------------------------------------------------

    /// <summary>
    /// A column of a CTE, derived table, temporary table or table variable: what it holds, never null where a filter proves
    /// it (Refinement.cs), and null too on an outer join's optional side unless a filter proves the row matched.
    /// </summary>
    private Value FromLocal(BoundLocalColumn bound, SourceLocation at)
    {
        var value = locals!.Column(bound.Relation, bound.Ordinal, at);
        if (value.Shape is not UnknownShape && bound.Ordinal >= 0 && Proved(bound.Source, Key(bound)))
            value = value with { NullableBecause = [] };
        if (bound.Source.OptionalSideOf is { } optional && !Matched(bound.Source) && !AlwaysApplied(bound))
            value = value with { NullableBecause = ShapeJoin.Reasons(value.NullableBecause, [new IrNullReason(bound.Source.OuterApply ? IrNullRules.OuterApply : IrNullRules.OuterJoin, optional.ToString())]) };
        if (Unmatched(bound.Source) is { } unmatched && value.Shape is not UnknownShape)
            value = value with { NullableBecause = ShapeJoin.Reasons(value.NullableBecause, [unmatched]) };
        if (Rolled(bound.Source) is { } rolled && value.Shape is not UnknownShape)
            value = value with { NullableBecause = ShapeJoin.Reasons(value.NullableBecause, [rolled]) };

        // A CASE branch that tested this very value proves it, whatever made it nullable (ProcedureAnalyzer.Case.cs).
        return value.Shape is not UnknownShape && bound.Ordinal >= 0 && BranchProved(bound.Source, Key(bound))
            ? value with { NullableBecause = [] }
            : value;
    }

    /// <summary>
    /// A derived table, <c>FROM (SELECT …) AS d (columns)</c>: read like a CTE of its own, so its values pass through
    /// unchanged and keep what its query proves of them. It may read the enclosing queries' columns, and an APPLY's left
    /// input's when it is (or is inside) the APPLY's right side. Its row count is its query's (Cardinality.cs), where it is
    /// read: for an APPLY's right side, for each outer row.
    /// </summary>
    private LocalRelationValues Derived(QueryDerivedTable derived, BindingScope? outer)
    {
        var at = At(derived);
        var name = derived.Alias?.Value ?? "(derived table)";
        if (derived.QueryExpression is not QuerySpecification query)
            return LocalRelationValues.Unknown(Unsupported(IrUnknownReasons.UnsupportedQuery, $"{name}: a derived table whose query is a UNION, INTERSECT, EXCEPT or parenthesised query is not modelled yet.", at));
        var row = Query(query, outer);
        return Relation(derived.Columns, row) is { } relation
            ? relation with { Rows = row.Rows, AtLeastOne = row.Rows == IrCardinality.One }
            : LocalRelationValues.Unknown(Unsupported(IrUnknownReasons.UnsupportedQuery, $"{name}: its columns cannot be listed: SELECT * is not expanded yet, and every column needs one name of its own.", at));
    }

    /// <summary>
    /// An integer literal within int's range is an int, never null; a larger one is numeric of its own digits (observed:
    /// A_literals_type_is_its_own_digits_or_characters). The parser gives <c>-2147483648</c> as one literal, which is int's
    /// least value, an int (observed in the same test).
    /// </summary>
    private Value Integer(IntegerLiteral literal)
    {
        if (long.TryParse(literal.Value, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var n) && n is >= int.MinValue and <= int.MaxValue)
            return new Value(SqlTypes.ToConvertedShape(model, new SqlType(SqlTypeKind.BuiltIn, "int", []), At(literal)), []);
        var digits = literal.Value.TrimStart('-', '+').TrimStart('0').Length;
        return digits <= 38
            ? new Value(SqlTypes.ToConvertedShape(model, new ScalarType("numeric", Precision: digits).ToSqlType(), At(literal)), [])
            : Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedExpression, $"The literal {literal.Value} has more than 38 digits.", At(literal)));
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
                return (null, new UnknownShape(IrUnknownReasons.VaryingResults, $"Result set {i + 1} is a rowset on some paths and JSON on others.", produced[0].At));

            var shapes = new List<Shape>();
            foreach (var shape in produced.Select(p => p.Result.Shape))
            {
                if (!shapes.Any(s => ShapeText.Of(s) == ShapeText.Of(shape)))
                    shapes.Add(shape);
            }

            var encoding = produced[0].Result.Encoding;
            var absent = produced.SelectMany(p => p.Result.AbsentBecause).ToList();
            if (i >= always)
                absent.AddRange(produced.Select(p => new IrNullReason(IrNullRules.NotRun, p.At.ToString())));
            // The fewest rows any statement here proves at most: a statement that may return many makes the position many.
            var cardinality = produced.Select(p => p.Result.Cardinality).Min();
            results.Add(new ResultShape(encoding, Union(shapes), [.. absent.Distinct()], cardinality));
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
        return shapes.FirstOrDefault(s => s is UnknownShape) ?? new UnionShape(shapes);
    }
}
