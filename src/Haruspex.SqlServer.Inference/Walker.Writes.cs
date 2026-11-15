using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using static Haruspex.SqlServer.Inference.Typer;

namespace Haruspex.SqlServer.Inference;

// DML on permanent tables, OUTPUT, triggers and cursors. README.md in this folder ("DML" and "Cursors") gives the
// model; the observations it rests on are DmlObservationTests in the differential tests.
internal sealed partial class Walker
{
    /// <summary>
    /// A write: <c>INSERT</c>, <c>UPDATE</c>, <c>DELETE</c> or <c>MERGE</c>, of any target. It returns no result set
    /// (observed: Dml_on_a_permanent_table_returns_no_result_set) unless it has an <c>OUTPUT</c> without <c>INTO</c>, or fires
    /// a trigger that may return one (<see cref="DmlTriggers"/>), which makes the procedure's results unknown. What it does to
    /// a temporary table is <paramref name="write"/>'s; a temporary table it changes through a CTE becomes unknown.
    /// </summary>
    /// <param name="statement">The statement.</param>
    /// <param name="spec">Its target and OUTPUT.</param>
    /// <param name="from">Its FROM (UPDATE, DELETE), whose sources OUTPUT may read.</param>
    /// <param name="where">Its WHERE (UPDATE, DELETE), which filters the rows OUTPUT returns.</param>
    /// <param name="with">Its WITH.</param>
    /// <param name="events">The statements it is, for triggers.</param>
    /// <param name="state">The state before it.</param>
    /// <param name="write">What it does to the temporary table or table variable it writes directly, given its target.</param>
    private Outcome Modify(
        TSqlStatement statement,
        DataModificationSpecification spec,
        FromClause? from,
        BooleanExpression? where,
        WithCtesAndXmlNamespaces? with,
        DmlEvents events,
        FlowState state,
        Func<DmlTarget, FlowState> write)
    {
        var at = At(statement);
        var target = DmlTarget.Of(spec.Target, from, with);
        if (target.Unmodelled is not null)
            return Blocked(statement);
        if (followsCalls && summaries.Triggers.Check(target, binder, events, at) is { } fired)
        {
            blocker ??= fired;
            return Outcome.Ends;
        }

        var after = write(target);

        // A temporary table changed underneath a CTE is not followed, so what it holds is not known.
        foreach (var name in target.Temps.Where(t => after.Temp(t) is KnownTable))
            after = after.WithTemp(name, Unknown(name, Unsupported(IrUnknownReason.UnsupportedStatement, $"It is written through a CTE or derived table at {at}, which is not followed.", at)));

        // So is a table variable's JSON: one a write through a CTE or derived table may change is no longer known.
        if (target.Direct is null)
            after = Underneath(after, spec.Target, with, at);

        return Continue(Output(statement, spec, from, where, with, target, state, after));
    }

    // ---- OUTPUT ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// A write's <c>OUTPUT</c>: one result set of the rows it wrote, many of them, and there even when no row is (observed:
    /// Output_without_into_returns_a_result_set_even_when_no_row_is_affected); with <c>INTO</c>, those rows go into a table
    /// instead and nothing is returned (Output_into_returns_nothing_and_writes_its_rows). It is read as a query over the
    /// write's FROM (or MERGE's source), filtered by its WHERE, with <c>inserted</c> and <c>deleted</c> as relations: the
    /// target after the write and before it.
    /// </summary>
    private FlowState Output(
        TSqlStatement statement,
        DataModificationSpecification spec,
        FromClause? from,
        BooleanExpression? where,
        WithCtesAndXmlNamespaces? with,
        DmlTarget target,
        FlowState before,
        FlowState after)
    {
        if (spec.OutputClause is null && spec.OutputIntoClause is null)
            return after;

        var at = At(statement);
        var merge = (spec as MergeSpecification)?.ActionClauses;
        var actions = merge?.Select(c => c.Action switch
        {
            InsertMergeAction => "INSERT",
            UpdateMergeAction => "UPDATE",
            DeleteMergeAction => "DELETE",
            _ => null,
        }).ToList();

        // Observed: Merge_output_has_null_deleted_columns_for_inserted_rows_null_inserted_columns_for_deleted_rows_and_null_source_columns_when_not_matched_by_source.
        var because = new IrNullReason(IrNullRule.MissingForAction, at.ToString());
        var inserted = spec is DeleteSpecification ? null : typer.Pseudo(spec, target, Reading(after), actions?.Contains("DELETE") == true ? because : null, at);
        var deleted = spec is InsertSpecification ? null : typer.Pseudo(spec, target, Reading(before), actions?.Contains("INSERT") == true ? because : null, at);

        var written = typer.Written(target, Reading(before));
        EquatableArray<string>? mergeActions = actions is null || actions.Contains(null) ? null : [.. actions.OfType<string>().Distinct().Order(StringComparer.Ordinal)];

        // Read in the state before the write: OUTPUT's own columns are inserted and deleted, which are given.
        if (spec.OutputClause is { } output)
        {
            var row = typer.OutputRow(spec, output, output.SelectColumns, from, where, with, inserted, deleted, written, mergeActions, because, Reading(before));
            appended++;
            after = after.WithResults(after.Results.Append(new ResultShape(IrEncoding.Rowset, Rowset(row), [], IrCardinality.Many), at));
        }

        if (spec.OutputIntoClause is { } into)
            after = OutputInto(into, typer.OutputRow(spec, into, into.SelectColumns, from, where, with, inserted, deleted, written, mergeActions, because, Reading(before)), after, at);

        return after;
    }

    /// <summary>
    /// <c>OUTPUT … INTO</c>: the rows go into a table as an INSERT would put them. A temporary table takes them like an
    /// INSERT's; a table variable is its declaration; a permanent table's own triggers never run, since OUTPUT … INTO a table
    /// with an enabled trigger fails (observed: Output_into_a_table_with_an_enabled_trigger_fails).
    /// </summary>
    private FlowState OutputInto(OutputIntoClause into, Row row, FlowState state, SourceLocation at)
    {
        var local = DmlTarget.Local(into.IntoTable);
        if (local is { IsVariable: true } variable)
        {
            var columns = TableVariableTargets(variable, into.IntoTableColumns);
            return row.Unlisted is null && columns is not null && columns.Count == row.Columns.Count
                ? TableVariableWritten(state, variable, columns, [[.. row.Columns.Select(c => c.Value)]], [[.. row.Columns.Select(c => c.Expression)]], at)
                : TableVariableWritten(state, variable, null, null, null, at);
        }

        if (local is not { } table)
            return state;
        if (state.Temp(table) is not KnownTable known)
            return state;
        if (row.Unlisted is not null || (into.IntoTableColumns.Count == 0 && known.Columns.Any(c => c.Identity != false)))
            return state.WithTemp(table, Unknown(table, Unsupported(IrUnknownReason.UnsupportedStatement, $"Which columns the OUTPUT at {at} writes in {table} is not modelled.", at)));
        var targets = into.IntoTableColumns.Count == 0
            ? [.. Enumerable.Range(0, known.Columns.Count)]
            : Binder.Written(into.IntoTableColumns, [.. known.Columns.Select(c => c.Name)]).ToList();
        if (targets.Any(i => i < 0) || targets.Count != row.Columns.Count)
            return state.WithTemp(table, Unknown(table, Unsupported(IrUnknownReason.UnresolvedColumn, $"The OUTPUT at {at} does not match the columns of {table} one to one.", at)));
        return state.WithTemp(table, Written(known, targets, [[.. row.Columns.Select(c => c.Value)]], at, [[.. row.Columns.Select(c => c.Expression)]]));
    }

    // ---- cursors --------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>FETCH</c>. Without <c>INTO</c> it returns the row as a result set, with a column the server marks hidden
    /// (<c>ROWSTAT</c>), which SqlClient lists and a driver that honours the flag leaves out (observed:
    /// Cursor_statements_and_fetch_into_return_nothing_and_fetch_without_into_returns_one_row): which keys the row has
    /// depends on the client, so the results are unknown. With <c>INTO</c> it returns nothing and assigns the variables,
    /// or leaves them as they were past the end (Fetch_into_leaves_the_variables_as_they_were_past_the_end). A keyset row
    /// deleted after OPEN (-2) assigns NULL, or zero or blanks for a NOT NULL column whatever its CHECK says
    /// (Fetch_into_from_a_keyset_cursor_whose_row_was_deleted_assigns_null_or_zeros_whatever_the_checks_say): within the
    /// column's type and nullability, which is all a variable keeps (never a CHECK's facets), so the join is still sound.
    /// </summary>
    private Outcome Fetch(FetchCursorStatement fetch, FlowState state)
    {
        var at = At(fetch);
        // Which columns the row has depends on the driver (docs/decisions/0015-fetch-without-into-is-unknown.md).
        if (fetch.IntoVariables.Count == 0)
        {
            blocker ??= new UnknownShape(IrUnknownReason.HiddenColumn, $"FETCH without INTO at {at} returns the row with a hidden ROWSTAT column, which some drivers list among its columns and others leave out.", at);
            return Outcome.Ends;
        }

        var row = FetchRow(fetch, state);
        for (var i = 0; i < fetch.IntoVariables.Count; i++)
        {
            var value = row is not null && row.Count == fetch.IntoVariables.Count
                ? row[i]
                : Value.Unknown(Unsupported(IrUnknownReason.UnsupportedStatement, $"The row FETCH at {at} reads is not known: its cursor is not one declared once in this procedure with a query whose columns can be listed.", at));
            state = Assign(state, Resolved(fetch.IntoVariables[i]).Name, value, definitely: false, at);
        }

        return Continue(state);
    }

    /// <summary>
    /// The values a FETCH reads, by column; null when its cursor is not known. A cursor declared once in this procedure is
    /// that declaration's query, read where the FETCH is: a dynamic or keyset cursor reads the rows as they are when
    /// fetched (observed: A_dynamic_cursor_fetches_a_temporary_tables_rows_as_they_are_when_fetched,
    /// A_keyset_cursor_fetches_a_row_changed_after_open_that_its_where_no_longer_keeps), so no filter proves anything of them,
    /// and a temporary table it reads holds then at least what it held when the cursor was opened, as writes only widen
    /// what a column holds; unless the table is altered, dropped or created again after the declaration, by this procedure
    /// or one it calls, when the row is not known. A cursor variable, or one declared elsewhere or more than once, is not
    /// known either; a cursor is declared when its DECLARE runs, not when the procedure is compiled (observed:
    /// A_cursor_is_declared_when_its_declare_runs_not_when_the_procedure_is_compiled), so a FETCH where none ran fails.
    /// </summary>
    private List<Value>? FetchRow(FetchCursorStatement fetch, FlowState state)
    {
        var declared = names.Declarations.Cursors(fetch.Cursor);
        if (declared.Count == 0)
            return null;

        // FETCH … GLOBAL c names the global cursor; a declaration says GLOBAL, or takes the database's default, which may be
        // LOCAL. So only an explicit GLOBAL is known to be it.
        var candidates = fetch.Cursor.IsGlobal
            ? [.. declared.Where(d => d.CursorDefinition.Options.Any(o => o.OptionKind == CursorOptionKind.Global))]
            : declared;
        if (candidates is not [var declaration] || declaration.CursorDefinition.Select is not { QueryExpression: QuerySpecification query } select)
            return null;

        var temps = DmlTarget.NamesRead(query).Select(Binder.Temporary).OfType<LocalName>().ToHashSet();
        if (temps.Count > 0 && Restructured(temps, declaration))
            return null;

        return typer.WithCtes(select.WithCtesAndXmlNamespaces, Reading(state).Fetching(), scope => typer.Outermost(query, scope) is { Unlisted: null } row ? row.Columns.Select(c => c.Value).ToList() : null, _ => null);
    }

    /// <summary>
    /// Whether a temporary table a cursor reads may be altered, dropped or made again after its declaration: by this
    /// procedure's ALTER or DROP anywhere, its CREATE or SELECT … INTO after the DECLARE, or anything a procedure it calls
    /// does to the table.
    /// </summary>
    private bool Restructured(HashSet<LocalName> temps, DeclareCursorStatement declaration)
    {
        var changes = new Restructures();
        body!.Accept(changes); // Never null: a FETCH is read while the body is walked, which sets it.
        if (changes.Altered.Overlaps(temps) || changes.Created.Any(c => temps.Contains(c.Name) && c.After > declaration.LastTokenIndex))
            return true;
        foreach (var call in changes.Calls)
        {
            // In a function's body, which has no calls to follow, a call is not followed either (one of a procedure fails there,
            // 557, and an extended stored procedure's is not read): the row is not known.
            if (!followsCalls || binder.BindCall(call) is not ProcedureCall { Procedure: var callee } || summaries.TempWrites(callee.Name).Overlaps(temps))
                return true;
        }

        return false;
    }

    private sealed class Restructures : TSqlFragmentVisitor
    {
        public HashSet<LocalName> Altered { get; } = [];

        public List<(LocalName Name, int After)> Created { get; } = [];

        public List<ExecuteSpecification> Calls { get; } = [];

        public override void Visit(AlterTableStatement node) => Add(Altered, node.SchemaObjectName);

        public override void Visit(DropTableStatement node)
        {
            foreach (var name in node.Objects)
                Add(Altered, name);
        }

        public override void Visit(CreateTableStatement node) => Create(node.SchemaObjectName, node);

        public override void Visit(SelectStatement node)
        {
            if (node.Into is not null)
                Create(node.Into, node);
        }

        public override void Visit(ExecuteSpecification node) => Calls.Add(node);

        // Only a local temporary table can be one a cursor reads that the walk follows, so only those are listed.
        private static void Add(HashSet<LocalName> names, SchemaObjectName name)
        {
            if (Binder.Temporary(name) is { } temporary)
                names.Add(temporary);
        }

        private void Create(SchemaObjectName name, TSqlFragment statement)
        {
            if (Binder.Temporary(name) is { } temporary)
                Created.Add((temporary, statement.FirstTokenIndex));
        }
    }
}
