using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// DML on permanent tables, OUTPUT, triggers and cursors. README.md in this folder ("DML" and "Cursors") gives the
// model; the observations it rests on are DmlObservationTests in the differential tests.
internal sealed partial class ProcedureAnalyzer
{
    /// <summary>While a MERGE's OUTPUT is read: the actions it takes, the closed set its <c>$action</c> is drawn from.</summary>
    private IReadOnlyList<string>? mergeActions;

    /// <summary>
    /// While an OUTPUT is read: the alias of the source in FROM the write changes, which OUTPUT may name, but whether as
    /// the row before or after the write is not observed, so a column read through it is unknown.
    /// </summary>
    private string? outputTarget;

    /// <summary>
    /// While a MERGE's OUTPUT is read: the alias of its source, whose columns are null for the rows it changes because they
    /// are not matched by the source, when it has such a clause; and why.
    /// </summary>
    private (HashSet<string> Aliases, IrNullReason Why)? unmatchedSource;

    /// <summary>While a cursor's query is read: no filter proves anything of the row a FETCH returns (see <see cref="FetchRow"/>).</summary>
    private bool unfiltered;

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
        if (calls?.Triggers.Check(target, binder, events, at) is { } fired)
        {
            blocker ??= fired;
            return Outcome.Ends;
        }

        var after = write(target);

        // A temporary table changed underneath a CTE is not followed, so what it holds is not known.
        foreach (var name in target.Temps.Where(t => after.Temp(t) is KnownTable))
            after = after.WithTemp(name, Unknown(name, Unsupported(IrUnknownReasons.UnsupportedStatement, $"It is written through a CTE or derived table at {at}, which is not followed.", at)));

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
        var because = new IrNullReason(IrNullRules.MergeAction, at.ToString());
        var inserted = spec is DeleteSpecification ? null : Pseudo(spec, target, after, actions?.Contains("DELETE") == true ? because : null, at);
        var deleted = spec is InsertSpecification ? null : Pseudo(spec, target, before, actions?.Contains("INSERT") == true ? because : null, at);

        var sources = new List<TableReference>();
        if (from is not null)
            sources.AddRange(from.TableReferences);
        if (spec is MergeSpecification { TableReference: { } source })
            sources.Add(source);

        var outerActions = mergeActions;
        var outerTarget = outputTarget;
        var outerUnmatched = unmatchedSource;
        mergeActions = actions is null || actions.Contains(null) ? null : [.. actions.OfType<string>().Distinct().Order(StringComparer.Ordinal)];
        outputTarget = from is not null && spec.Target is NamedTableReference { SchemaObject: { SchemaIdentifier: null } written } ? written.BaseIdentifier.Value : null;
        // Every source in the USING tree: a joined USING is null-extended as a whole.
        unmatchedSource = spec is MergeSpecification { TableReference: { } merged } m
            && m.ActionClauses.Any(c => c.Condition == MergeCondition.NotMatchedBySource)
            ? (DmlTarget.Sources([merged]).Select(DmlTarget.Alias).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase), because)
            : null;
        locals!.State = before;
        try
        {
            if (spec.OutputClause is { } output)
            {
                var row = OutputRow(output.SelectColumns, sources, where, with, inserted, deleted, at);
                appended++;
                after = after.WithResults(after.Results.Append(new ResultShape(IrEncoding.Rowset, Rowset(row), [], IrCardinality.Many), at));
            }

            if (spec.OutputIntoClause is { } into)
                after = OutputInto(into, OutputRow(into.SelectColumns, sources, where, with, inserted, deleted, at), after, at);
        }
        finally
        {
            mergeActions = outerActions;
            outputTarget = outerTarget;
            unmatchedSource = outerUnmatched;
            locals.State = after;
        }

        return after;
    }

    /// <summary>
    /// What <c>inserted</c> or <c>deleted</c> holds: the target's columns in <paramref name="state"/>, which for a permanent
    /// table is its declaration (every value is converted to the column, observed:
    /// Output_columns_are_the_values_converted_to_the_target_columns), and null too for the MERGE actions that give it none.
    /// </summary>
    private LocalRelationValues Pseudo(DataModificationSpecification spec, DmlTarget target, FlowState state, IrNullReason? merged, SourceLocation at)
    {
        LocalRelationValues values;
        if (target.Direct is { } direct)
        {
            locals!.State = state;
            values = locals.Find(direct) is { } relation ? locals.Values(relation) : LocalRelationValues.Unknown(Unsupported(IrUnknownReasons.UnresolvedTable, $"{direct} is not declared.", at));
        }
        else if (target.Named is { } only
            && binder.ResolveWritten(only) is [var name] && model.FindTable(name) is { } table && model.FindTableRedefinition(name) is null)
        {
            var source = new BoundSource(name.Name, table, null, null);
            values = new LocalRelationValues([.. table.Columns.Select(c => c.Name)], [.. table.Columns.Select(c => FromColumn(new BoundColumn(source, table, c)))], null);
        }
        else
        {
            values = LocalRelationValues.Unknown(Unsupported(IrUnknownReasons.UnsupportedStatement, target.Named is { } through
                ? $"The OUTPUT at {at} of a write to {Text(through)}, which is not one table of the project (a view, a synonym, a table defined elsewhere or more than one), is not modelled."
                : $"The OUTPUT at {at} of a write through a CTE or derived table, {Text(spec.Target)}, is not modelled.", at));
        }

        if (merged is null || values.Values is null)
            return values;
        return values with { Values = [.. values.Values.Select(v => v.Shape is UnknownShape ? v : v with { NullableBecause = ShapeJoin.Reasons(v.NullableBecause, [merged]) })] };
    }

    /// <summary>
    /// The row an OUTPUT's columns give, read as the select list of a query over the write's sources and the
    /// <c>inserted</c> and <c>deleted</c> relations, filtered by its WHERE. <c>inserted.*</c> and <c>deleted.*</c> list the
    /// target's columns.
    /// </summary>
    private Row OutputRow(IList<SelectElement> columns, List<TableReference> sources, BooleanExpression? where, WithCtesAndXmlNamespaces? with, LocalRelationValues? inserted, LocalRelationValues? deleted, SourceLocation at)
    {
        return WithCtes(with, () =>
        {
            var frame = locals!.PushCtes();
            try
            {
                var query = new QuerySpecification { FromClause = new FromClause(), WhereClause = where is null ? null : new WhereClause { SearchCondition = where } };
                foreach (var source in sources)
                    query.FromClause.TableReferences.Add(source);
                foreach (var (name, values) in new[] { ("inserted", inserted), ("deleted", deleted) })
                {
                    if (values is null)
                        continue;
                    frame[name] = locals.Register(name, values);
                    query.FromClause.TableReferences.Add(Like(new NamedTableReference { SchemaObject = Name(name) }, columns[0]));
                }

                foreach (var column in columns)
                {
                    if (column is SelectStarExpression { Qualifier.Identifiers: [var qualifier] } star
                        && (qualifier.Value.Equals("inserted", StringComparison.OrdinalIgnoreCase) ? inserted : qualifier.Value.Equals("deleted", StringComparison.OrdinalIgnoreCase) ? deleted : null) is { Names: { } names })
                    {
                        foreach (var name in names)
                        {
                            var reference = Like(new ColumnReferenceExpression { MultiPartIdentifier = new MultiPartIdentifier() }, star);
                            reference.MultiPartIdentifier.Identifiers.Add(new Identifier { Value = qualifier.Value });
                            reference.MultiPartIdentifier.Identifiers.Add(new Identifier { Value = name });
                            query.SelectElements.Add(Like(new SelectScalarExpression { Expression = reference }, star));
                        }
                    }
                    else
                    {
                        query.SelectElements.Add(column);
                    }
                }

                return Query(query, parent: null);
            }
            finally
            {
                locals.PopCtes();
            }
        }, why => new Row([new RowColumn(null, Value.Unknown(why), at)], null));

        static SchemaObjectName Name(string name)
        {
            var schemaObject = new SchemaObjectName();
            schemaObject.Identifiers.Add(new Identifier { Value = name });
            return schemaObject;
        }
    }

    /// <summary>A fragment made here, placed where <paramref name="like"/> is, so that locations and texts name the source.</summary>
    private static T Like<T>(T fragment, TSqlFragment like)
        where T : TSqlFragment
    {
        fragment.ScriptTokenStream = like.ScriptTokenStream;
        fragment.FirstTokenIndex = like.FirstTokenIndex;
        fragment.LastTokenIndex = like.LastTokenIndex;
        return fragment;
    }

    /// <summary>
    /// <c>OUTPUT … INTO</c>: the rows go into a table as an INSERT would put them. A temporary table takes them like an
    /// INSERT's; a table variable is its declaration; a permanent table's own triggers never run, since OUTPUT … INTO a table
    /// with an enabled trigger fails (observed: Output_into_a_table_with_an_enabled_trigger_fails).
    /// </summary>
    private FlowState OutputInto(OutputIntoClause into, Row row, FlowState state, SourceLocation at)
    {
        if (into.IntoTable is NamedTableReference { SchemaObject: { SchemaIdentifier: null, BaseIdentifier.Value: var variable } } && variable.StartsWith('@'))
        {
            var columns = TableVariableTargets(variable, into.IntoTableColumns);
            return row.Star is null && columns is not null && columns.Count == row.Columns.Count
                ? TableVariableWritten(state, variable, columns, [[.. row.Columns.Select(c => c.Value)]], [[.. row.Columns.Select(c => c.Expression)]], at)
                : TableVariableWritten(state, variable, null, null, null, at);
        }

        if (into.IntoTable is not NamedTableReference { SchemaObject: var name } || !IsTemporary(name))
            return state;
        var table = name.BaseIdentifier.Value;
        if (state.Temp(table) is not KnownTable known)
            return state;
        if (row.Star is not null || (into.IntoTableColumns.Count == 0 && known.Columns.Any(c => c.Identity != false)))
            return state.WithTemp(table, Unknown(table, Unsupported(IrUnknownReasons.UnsupportedStatement, $"Which columns the OUTPUT at {at} writes in {table} is not modelled.", at)));
        var targets = into.IntoTableColumns.Count == 0
            ? [.. Enumerable.Range(0, known.Columns.Count)]
            : into.IntoTableColumns.Select(c => IndexOf(known, c.MultiPartIdentifier.Identifiers[^1].Value)).ToList();
        if (targets.Any(i => i < 0) || targets.Count != row.Columns.Count)
            return state.WithTemp(table, Unknown(table, Unsupported(IrUnknownReasons.UnresolvedColumn, $"The OUTPUT at {at} does not match the columns of {table} one to one.", at)));
        return state.WithTemp(table, Written(known, targets, [[.. row.Columns.Select(c => c.Value)]], at, [[.. row.Columns.Select(c => c.Expression)]]));
    }

    /// <summary>
    /// <c>$action</c> in a MERGE's OUTPUT: an <c>nvarchar(10)</c>, never null (observed: Merge_action_is_an_nvarchar_10), one of
    /// the actions the statement has.
    /// </summary>
    private Value MergeAction(ColumnReferenceExpression action)
    {
        var at = At(action);
        if (mergeActions is null)
            return Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedExpression, $"{Text(action)} outside a MERGE's OUTPUT is not modelled.", at));
        var shape = (ScalarShape)SqlTypes.ToConvertedShape(model, new SqlType(SqlTypeKind.BuiltIn, "nvarchar", ["10"]), at);
        var values = new IrValueSet([.. mergeActions.Select(a => new IrValue(a, IsString: true))], IrValueMatch.Exact, [new IrFactSource(IrFactSources.Engine, "$action")]);
        return new Value(shape with { Type = shape.Type with { Facets = (shape.Type.Facets ?? new IrFacets()) with { Values = values } } }, []);
    }

    /// <summary>Whether a column read in an OUTPUT names the write's target through its alias, which is not modelled.</summary>
    private bool ReadsOutputTarget(ColumnBinding binding)
    {
        var source = binding switch
        {
            BoundColumn column => column.Source,
            BoundLocalColumn local => local.Source,
            _ => null,
        };
        return outputTarget is { } alias && source?.Alias is { } bound && bound.Equals(alias, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Why a column of a MERGE's source may be null in its OUTPUT: the rows it changes not matched by the source have none.</summary>
    private IrNullReason? Unmatched(BoundSource source) =>
        unmatchedSource is { } unmatched && source.Alias is { } alias && unmatched.Aliases.Contains(alias)
            && !(source.Local?.Name is "inserted" or "deleted")
            ? unmatched.Why
            : null;

    // ---- cursors --------------------------------------------------------------------------------------------------

    /// <summary>Every cursor the body declares by name (<c>DECLARE c CURSOR</c>), with each of its declarations.</summary>
    private readonly Dictionary<string, List<DeclareCursorStatement>> cursors = new(StringComparer.OrdinalIgnoreCase);

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
        if (fetch.IntoVariables.Count == 0)
        {
            blocker ??= new UnknownShape(IrUnknownReasons.HiddenColumn, $"FETCH without INTO at {at} returns the row with a hidden ROWSTAT column, which some drivers list among its columns and others leave out.", at);
            return Outcome.Ends;
        }

        var row = FetchRow(fetch);
        for (var i = 0; i < fetch.IntoVariables.Count; i++)
        {
            var value = row is not null && row.Count == fetch.IntoVariables.Count
                ? row[i]
                : Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedStatement, $"The row FETCH at {at} reads is not known: its cursor is not one declared once in this procedure with a query whose columns can be listed.", at));
            state = Assign(state, fetch.IntoVariables[i].Name, value, definitely: false, at);
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
    private List<Value>? FetchRow(FetchCursorStatement fetch)
    {
        if (fetch.Cursor?.Name?.Identifier is not { } name || !cursors.TryGetValue(name.Value, out var declared))
            return null;

        // FETCH … GLOBAL c names the global cursor; a declaration says GLOBAL, or takes the database's default, which may be
        // LOCAL. So only an explicit GLOBAL is known to be it.
        var candidates = fetch.Cursor.IsGlobal
            ? [.. declared.Where(d => d.CursorDefinition.Options.Any(o => o.OptionKind == CursorOptionKind.Global))]
            : declared;
        if (candidates is not [var declaration] || declaration.CursorDefinition.Select is not { QueryExpression: QuerySpecification query } select)
            return null;

        var reads = new DmlTarget.NamedTables();
        query.Accept(reads);
        var temps = reads.Found.Where(IsTemporary).Select(n => n.BaseIdentifier.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (temps.Count > 0 && Restructured(temps, declaration))
            return null;

        var outer = unfiltered;
        unfiltered = true;
        try
        {
            return WithCtes(select.WithCtesAndXmlNamespaces, () => Query(query, parent: null) is { Star: null } row ? row.Columns.Select(c => c.Value).ToList() : null, _ => null);
        }
        finally
        {
            unfiltered = outer;
        }
    }

    /// <summary>
    /// Whether a temporary table a cursor reads may be altered, dropped or made again after its declaration: by this
    /// procedure's ALTER or DROP anywhere, its CREATE or SELECT … INTO after the DECLARE, or anything a procedure it calls
    /// does to the table.
    /// </summary>
    private bool Restructured(HashSet<string> temps, DeclareCursorStatement declaration)
    {
        var changes = new Restructures();
        body!.Accept(changes);
        if (changes.Altered.Overlaps(temps) || changes.Created.Any(c => temps.Contains(c.Name) && c.After > declaration.LastTokenIndex))
            return true;
        foreach (var call in changes.Calls)
        {
            if (binder.BindCall(call) is not ProcedureCall { Procedure: var callee } || calls!.TempWrites(callee.Name).Overlaps(temps))
                return true;
        }

        return false;
    }

    private sealed class Restructures : TSqlFragmentVisitor
    {
        public HashSet<string> Altered { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Name, int After)> Created { get; } = [];

        public List<ExecuteSpecification> Calls { get; } = [];

        public override void Visit(AlterTableStatement node) => Altered.Add(node.SchemaObjectName.BaseIdentifier.Value);

        public override void Visit(DropTableStatement node)
        {
            foreach (var name in node.Objects)
                Altered.Add(name.BaseIdentifier.Value);
        }

        public override void Visit(CreateTableStatement node) => Created.Add((node.SchemaObjectName.BaseIdentifier.Value, node.FirstTokenIndex));

        public override void Visit(SelectStatement node)
        {
            if (node.Into is not null)
                Created.Add((node.Into.BaseIdentifier.Value, node.FirstTokenIndex));
        }

        public override void Visit(ExecuteSpecification node) => Calls.Add(node);
    }
}
