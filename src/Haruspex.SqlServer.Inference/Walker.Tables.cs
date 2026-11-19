using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using static Haruspex.SqlServer.Inference.Typer;

namespace Haruspex.SqlServer.Inference;

// What writes do to the body's own tables: SELECT … INTO, INSERT, UPDATE and MERGE of a temporary table or a table variable.
// Creating and altering a temporary table is Walker.TableDefinitions.cs. README.md in this folder ("The state", "DML") gives
// the rules and the observations they rest on (StatementFlowObservationTests and DmlObservationTests in the differential tests).
internal sealed partial class Walker
{
    /// <summary>
    /// <c>SELECT … INTO #t</c>: a new table whose columns are the select's, each holding what the select gives, with the
    /// facts and origins of a value that passes through unchanged. JSON is text once stored (observed), unless its SQL type
    /// is json, which is not tracked, so live JSON stored here is unknown.
    /// </summary>
    private FlowState SelectInto(SelectStatement select, FlowState state)
    {
        if (Binder.Temporary(select.Into) is not { } name)
        {
            Blocked(select);
            return state;
        }

        return MadeFrom(select, name, state, "SELECT … INTO");
    }

    /// <summary>
    /// A temporary table made from a query, by <c>SELECT … INTO</c> or <c>CREATE TABLE … AS SELECT</c>, which make the same columns
    /// from the same query: their types, nullability and IDENTITY (observed:
    /// A_temporary_table_made_from_a_query_has_the_columns_select_into_makes).
    /// </summary>
    /// <param name="select">The query, with its WITH.</param>
    /// <param name="name">The table it makes.</param>
    /// <param name="state">The state before it.</param>
    /// <param name="how">The statement, for a message.</param>
    private FlowState MadeFrom(SelectStatement select, LocalName name, FlowState state, string how)
    {
        var at = At(select);
        var table = typer.WithCtes<TempTable>(select.WithCtesAndXmlNamespaces, Reading(state), scope =>
        {
            var row = typer.Outermost(select.QueryExpression, scope);
            if (row.Unlisted is { } unlisted)
                return Unknown(name, unlisted);
            if (row.Columns.Any(c => string.IsNullOrEmpty(c.Name)) || row.Columns.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                return Unknown(name, Unsupported(IrUnknownReason.UnnamedColumn, $"{how} {name} has a column without a name, or two of one name, which SQL Server refuses.", at));
            // Through a set operation SQL Server does not copy IDENTITY (observed: Select_into_from_a_union_does_not_copy_an_identity).
            var identity = select.QueryExpression is QuerySpecification query ? Identities(query, scope) : new bool?[row.Columns.Count];
            // Never null: a column without a name was refused above.
            return new KnownTable(name, [.. row.Columns.Select((c, i) => new TempColumn(c.Name!, Stored(c.Value, $"{name}.{c.Name}", c.At, c.Expression), null, identity[i]))]);
        }, why => Unknown(name, why));
        return state.WithTemp(name, table);
    }

    private static UnknownTable Unknown(LocalName name, UnknownShape why) => new(why with { Detail = $"{name}: {why.Detail}" });

    /// <summary>
    /// Whether each column <c>SELECT … INTO</c> makes is an identity column: SQL Server copies the IDENTITY property of a
    /// column read unchanged from one table (observed), which matters to an insert without a column list, which skips it.
    /// It is documented not to be copied through an expression, so a column that is not a plain column reference is not one.
    /// Through a join, a column listed twice, or a relation whose columns' properties are not known (a CTE, a table
    /// variable, a caller's table), it may be: null.
    /// </summary>
    /// <param name="query">The SELECT … INTO's query.</param>
    /// <param name="at">The statement's scope, its WITH's CTEs included.</param>
    private bool?[] Identities(QuerySpecification query, ReadScope at)
    {
        var scope = ((BoundSpecification)binder.Bind(query, null, at.Names)).Scope;
        var single = query.FromClause?.TableReferences is [NamedTableReference];
        // Each column the query gives, with what it reads when it is a column read unchanged: a star's columns are (observed:
        // Select_star_into_copies_an_identity_from_one_table).
        var columns = new List<(Resolution? Bound, bool Parenthesised)>();
        foreach (var element in query.SelectElements)
        {
            switch (element)
            {
                case SelectScalarExpression scalar:
                    columns.Add((scalar.Expression is ColumnReferenceExpression column ? binder.Resolve(column, scope) : null, scalar.Expression is ParenthesisExpression));
                    break;
                case SelectStarExpression star when binder.Star(star, scope) is ExpandedStar { Columns: var expanded }:
                    columns.AddRange(expanded.Select(c => ((Resolution?)c.Column, false)));
                    break;
            }
        }

        var bound = columns.Select(c => c.Bound).ToList();
        return [.. columns.Select((listed, i) =>
        {
            if (bound[i] is not { } binding)
                return listed.Parenthesised ? null : false;
            var identity = binding switch
            {
                TableColumn column => column.Column.IsIdentity,
                RelationColumn { Relation: TemporaryTable temporary, Ordinal: var ordinal }
                    when at.State.Temp(temporary.Local) is KnownTable known => known.Columns[ordinal].Identity,
                _ => null,
            };

            // Listed twice, the property is documented not to be copied; claimed only of a column listed once.
            var once = bound.Count(b => Same(b, binding)) == 1;
            return identity == false ? false : single && once ? identity : null;
        })];

        static bool Same(Resolution? a, Resolution b) => (a, b) switch
        {
            (TableColumn x, TableColumn y) => ReferenceEquals(x.Column, y.Column),
            (RelationColumn x, RelationColumn y) => ReferenceEquals(x.Relation, y.Relation) && x.Ordinal == y.Ordinal,
            _ => ReferenceEquals(a, b),
        };
    }

    /// <param name="expression">
    /// The expression stored, when <c>SELECT … INTO</c> makes the column from it: a FOR JSON subquery or a constructor not
    /// <c>RETURNING json</c> makes an <c>nvarchar(max)</c> column, holding the JSON as text (observed:
    /// Select_into_makes_nvarchar_max_of_built_json_and_json_of_returning_json).
    /// </param>
    /// <remarks>
    /// A <c>char(n)</c> or <c>binary(n)</c> value stored here is at most its length: the column the statement makes may be
    /// nullable whatever the value (the server decides), and a nullable column of either type, of a temporary table made while
    /// the caller's session has ANSI_PADDING off, keeps its values with their padding cut
    /// (<see cref="ScalarType.IsPaddingCutWithoutAnsiPadding"/>).
    /// </remarks>
    private static Value Stored(Value value, string column, SourceLocation at, ScalarExpression? expression = null) => value.IsLiveJson
        ? expression is not null && BuiltAsText(expression) && value.Shape is not UnknownShape
            ? new Value(new JsonTextShape(value.Shape), value.NullableBecause)
            : Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"JSON stored in {column} is text unless its SQL type is json, which is not tracked yet.", at))
        : value.Shape is ScalarShape { BuiltIn.IsPaddingCutWithoutAnsiPadding: true, Type.Facets: { Length: { IsFixed: true } length } facets } padded
            ? value with { Shape = padded.WithFacets(facets with { Length = length with { IsFixed = false } }) }
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
        if (target.Direct is { IsVariable: true } variable)
            return TableVariableInsert(insert, variable, state);
        if (target.Direct is not { IsTemporary: true } name || state.Temp(name) is not KnownTable table)
            return state;

        var at = At(insert);

        var targets = InsertTargets(table, spec.Columns, spec.InsertSource);
        if (targets.Any(i => i < 0))
            return state.WithTemp(name, Unknown(name, Unsupported(IrUnknownReason.UnresolvedColumn, "The insert names a column the table does not have.", at)));

        var rows = typer.WithCtes(insert.WithCtesAndXmlNamespaces, Reading(state), scope => InsertedRows(spec.InsertSource, targets.Count, at, scope), why => [[.. targets.Select(_ => Value.Unknown(why))]]);
        if (rows is null)
            return state.WithTemp(name, Unknown(name, Unsupported(IrUnknownReason.UnsupportedStatement, "The insert's values do not match its columns one to one.", at)));

        return state.WithTemp(name, Written(table, targets, rows, at, InsertedExpressions(spec.InsertSource, rows.Count)));
    }

    /// <summary>
    /// The column of the table each value of an inserted row goes to, by position, -1 for a name it doesn't have. DEFAULT VALUES
    /// writes no column: each gets its default, or null, and a column SELECT … INTO made has no default.
    /// </summary>
    private static List<int> InsertTargets(KnownTable table, IList<ColumnReferenceExpression> columns, InsertSource source) =>
        source is ValuesInsertSource { IsDefaultValues: true } ? []
            : columns.Count == 0 ? [.. Enumerable.Range(0, table.Columns.Count)]
            : [.. Binder.Written(columns, [.. table.Columns.Select(c => c.Name)])];

    /// <summary>
    /// The table after rows are inserted into it: a declared column is unchanged (the engine converts each value to its type
    /// and refuses a null where it is NOT NULL); a column SELECT … INTO made joins with each value, and with null when the
    /// insert leaves it out, unless it is an identity column, which takes its next value.
    /// </summary>
    /// <param name="targets">The table's column each value of a row goes to, by position.</param>
    /// <param name="expressions">Each row's expressions, by the same positions, when the rows are written from the source.</param>
    private KnownTable Written(KnownTable table, List<int> targets, IEnumerable<IReadOnlyList<Value>> rows, SourceLocation at, IReadOnlyList<IReadOnlyList<ScalarExpression?>>? expressions = null)
    {
        var all = rows.ToList();
        var columns = table.Columns.ToList();
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var where = $"{table.Name}.{column.Name}";
            var position = targets.IndexOf(i);
            if (column.Declaration is not null)
            {
                // A declared column's value is its declaration's, but what it holds as JSON is what is written. One the
                // insert leaves out takes its default, which is not read.
                if (column.Json is { } slot)
                    columns[i] = column with { Json = JsonSlot.Join(slot, JsonRows(all, expressions, position, at, where)) };
                continue;
            }

            // An identity column the insert leaves out gets its next value, never null.
            if (position < 0 && column.Identity == true)
                continue;
            var written = position < 0
                ? [Value.Computed(column.Value.Shape, [new IrNullReason(IrNullRule.NullableColumn, where)])]
                : all.Select(r => Stored(r[position], where, at));
            columns[i] = column with { Value = written.Aggregate(column.Value, (all, value) => Value.JoinConverted(all, value, at, where)) };
        }

        return table with { Columns = [.. columns] };
    }

    /// <summary>
    /// What rows written put in a traced column as JSON (<see cref="JsonSlot"/>): the join of each row's value at
    /// <paramref name="position"/>, or, when the write leaves the column out (a negative position), its default, which is not read.
    /// </summary>
    private JsonSlot JsonRows(IReadOnlyList<IReadOnlyList<Value>> rows, IReadOnlyList<IReadOnlyList<ScalarExpression?>>? expressions, int position, SourceLocation at, string where)
    {
        if (position < 0)
            return JsonSlot.Text(Unsupported(IrUnknownReason.JsonShapeUnknown, $"{where} is left out by the write {Text(at)}, so it takes its default, which is not read.", at));
        return rows.Select((r, n) => JsonWritten(r[position], expressions?[n][position], at, where)).Aggregate(JsonSlot.Empty, JsonSlot.Join);
    }

    /// <summary>
    /// The expressions each row of an insert writes, by position, where the source has them as written: a VALUES list's, or a
    /// query's select list (a set operation's are not read). Null otherwise; a value is then read as JSON by its shape alone.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<ScalarExpression?>>? InsertedExpressions(InsertSource source, int rows) => source switch
    {
        ValuesInsertSource { IsDefaultValues: false } values when values.RowValues.Count == rows =>
            [.. values.RowValues.Select(r => (IReadOnlyList<ScalarExpression?>)[.. r.ColumnValues])],
        SelectInsertSource { Select: QuerySpecification query } when rows == 1 && query.SelectElements.All(e => e is SelectScalarExpression) =>
            [[.. query.SelectElements.Cast<SelectScalarExpression>().Select(e => (ScalarExpression?)e.Expression)]],
        _ => null,
    };

    /// <summary>The values each row of an insert writes, by the insert's column positions; null when a row has the wrong number.</summary>
    /// <param name="source">The insert's source.</param>
    /// <param name="count">How many columns it writes.</param>
    /// <param name="at">Where the insert is.</param>
    /// <param name="scope">The statement's scope, its WITH's CTEs included.</param>
    private List<IReadOnlyList<Value>>? InsertedRows(InsertSource source, int count, SourceLocation at, ReadScope scope)
    {
        switch (source)
        {
            case ValuesInsertSource { IsDefaultValues: true }:
                return [];
            case ValuesInsertSource values:
                var rows = values.RowValues.Select(r => (IReadOnlyList<Value>)[.. r.ColumnValues.Select(v => typer.Expression(v, scope))]).ToList();
                return rows.All(r => r.Count == count) ? rows : null;
            case SelectInsertSource { Select: { } query }:
                var row = typer.Outermost(query, scope);
                if (row.Unlisted is { } unlisted)
                    return [[.. Enumerable.Range(0, count).Select(_ => Value.Unknown(unlisted))]];
                return row.Columns.Count == count ? [[.. row.Columns.Select(c => c.Value)]] : null;
            default:
                return [[.. Enumerable.Range(0, count).Select(_ => Value.Unknown(Unsupported(IrUnknownReason.UnsupportedQuery, "This insert's source is not modelled yet.", at)))]];
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
        var target = written.Direct;

        return typer.WithCtes(update.WithCtesAndXmlNamespaces, Reading(state), statement =>
            Set(spec.SetClauses, written, typer.Attach(statement.In(binder.BindUpdate(spec, statement.Names).Scope)), state, At(update)),
            why => Unread(spec.SetClauses, target, why, state, At(update)));
    }

    /// <summary>
    /// What an UPDATE's SET clauses do, an UPDATE's or a MERGE's: each variable they set may be set; each value written to a
    /// column SELECT … INTO made of the temporary table written directly joins with it, and a declared column is unchanged
    /// but for the JSON it holds; a table variable's traced column joins with the JSON written.
    /// </summary>
    /// <param name="clauses">The SET clauses.</param>
    /// <param name="written">The write's target.</param>
    /// <param name="scope">The scope their values are read in.</param>
    /// <param name="state">The state before them.</param>
    /// <param name="at">Where the statement is.</param>
    private FlowState Set(IEnumerable<SetClause> clauses, DmlTarget written, ReadScope scope, FlowState state, SourceLocation at)
    {
        var target = written.Direct;
        var table = target is { IsTemporary: true } temporary ? state.Temp(temporary) as KnownTable : null;
        var columns = table?.Columns.ToList();
        foreach (var clause in clauses)
        {
            if (clause is FunctionCallSetClause mutation && Binder.MutatedColumn(mutation) is { } mutated)
            {
                if (target is { IsVariable: true } changed && new ColumnSlot(changed, mutated) is var slotKey && state.Json.TryGetValue(slotKey, out var held))
                    state = state.WithJson(slotKey, JsonSlot.Join(held, Rewritten(mutation, slotKey.ToString())));
                if (table is null || columns is null)
                    continue;
                var m = IndexOf(table, mutated);
                if (m < 0)
                    return state.WithTemp(table.Name, Unknown(table.Name, Unsupported(IrUnknownReason.UnresolvedColumn, $"The update changes {mutated}, which {table.Name} does not have.", At(mutation))));
                var where = $"{table.Name}.{columns[m].Name}";
                columns[m] = columns[m].Declaration is null
                    ? columns[m] with { Value = Rewritten(columns[m].Value, where, At(mutation)) }
                    : columns[m].Json is { } slot ? columns[m] with { Json = JsonSlot.Join(slot, Rewritten(mutation, where)) } : columns[m];
                continue;
            }

            if (clause is not AssignmentSetClause set)
                return table is null ? state : state.WithTemp(table.Name, Unknown(table.Name, Unsupported(IrUnknownReason.UnsupportedStatement, $"The update {Text(clause)} is not modelled yet.", At(clause))));

            var value = typer.AssignedValue(set.AssignmentKind, set.NewValue, scope, set);
            if (set.Variable is { } variable)
                state = Assign(state, Resolved(variable).Name, value, definitely: false, At(set), set.NewValue);
            if (set.Column is not null && target is { IsVariable: true } tableVariable && new ColumnSlot(tableVariable, Binder.WrittenColumn(set.Column)) is var key
                && state.Json.TryGetValue(key, out var traced))
            {
                state = state.WithJson(key, JsonSlot.Join(traced, JsonWritten(value, set.NewValue, At(set), key.ToString())));
            }

            if (set.Column is null || table is null || columns is null)
                continue;

            var i = Binder.Written([set.Column], [.. table.Columns.Select(c => c.Name)])[0];
            if (i < 0)
                return state.WithTemp(table.Name, Unknown(table.Name, Unsupported(IrUnknownReason.UnresolvedColumn, $"The update sets {Text(set.Column)}, which {table.Name} does not have.", At(set))));
            if (columns[i].Declaration is null)
            {
                var where = $"{table.Name}.{columns[i].Name}";
                columns[i] = columns[i] with { Value = Value.JoinConverted(columns[i].Value, Stored(value, where, at), at, where) };
            }
            else if (columns[i].Json is { } slot)
            {
                columns[i] = columns[i] with { Json = JsonSlot.Join(slot, JsonWritten(value, set.NewValue, At(set), $"{table.Name}.{columns[i].Name}")) };
            }
        }

        return table is null || columns is null ? state : state.WithTemp(table.Name, table with { Columns = [.. columns] });
    }

    /// <summary>
    /// A column after <c>SET c.WRITE(…)</c> changes part of each value in place (observed:
    /// Write_changes_part_of_a_value_in_place_keeping_its_type_and_fails_on_null): of its type still, which must be a max type
    /// (258), and null exactly where it was, since a NULL is an error (5302); but its content is any string of the type, so
    /// only the type's facets hold, and text that held JSON built here is a string that may not.
    /// </summary>
    private static Value Rewritten(Value column, string where, SourceLocation at) => column.Shape switch
    {
        ScalarShape scalar => column with { Shape = scalar.WithFacets(ScalarFacets.TypeOnly(scalar.Type.Facets)) },
        JsonTextShape => column with { Shape = SqlTypes.ToConvertedShape(new ScalarType(BuiltInType.NVarChar, -1), at) },
        UnknownShape => column,
        _ => Value.Unknown(Unsupported(IrUnknownReason.UnsupportedStatement, $"{where} is changed in place by .WRITE, which is not modelled for what it holds.", at)),
    };

    /// <summary>What a place that held JSON built here holds after <c>SET c.WRITE(…)</c>: text that may not be JSON.</summary>
    private JsonSlot Rewritten(FunctionCallSetClause mutation, string where) =>
        JsonSlot.Text(Unsupported(IrUnknownReason.JsonShapeUnknown, $"{where} is changed in place by {Text(mutation)}, which is not JSON built here.", At(mutation)));

    /// <summary>
    /// What a MERGE does: each action writes as the statement it holds does, an INSERT its row (<see cref="Inserted"/>), an
    /// UPDATE its SET clauses (<see cref="Set"/>), a DELETE nothing a column may hold. Each action writes rows of its own, and
    /// a write only joins into what a column may hold, so the order they are read in doesn't matter. So into a temporary table
    /// or a table variable, as into a permanent one, and a variable an UPDATE sets may be set whatever the target (observed:
    /// Merge_into_a_temporary_table_writes_each_actions_rows_and_may_set_a_variable).
    /// </summary>
    private FlowState Merge(MergeStatement merge, DmlTarget written, FlowState state)
    {
        var spec = merge.MergeSpecification;
        var at = At(merge);
        return typer.WithCtes(merge.WithCtesAndXmlNamespaces, Reading(state), statement =>
        {
            var scope = typer.Attach(statement.In(binder.BindMerge(spec, statement.Names).Scope));
            var after = state;
            foreach (var clause in spec.ActionClauses)
            {
                after = clause.Action switch
                {
                    UpdateMergeAction update => Set(update.SetClauses, written, scope, after, at),
                    InsertMergeAction insert => Inserted(insert, written, scope, after, at),
                    _ => after,
                };
            }

            return after;
        }, why => Unread([.. spec.ActionClauses.Select(c => c.Action).OfType<UpdateMergeAction>().SelectMany(u => u.SetClauses)], written.Direct, why, state, at));
    }

    /// <summary>
    /// A MERGE's INSERT action: its row written to a temporary table, as an INSERT's (<see cref="Written"/>), or to a table
    /// variable's traced columns.
    /// </summary>
    private FlowState Inserted(InsertMergeAction insert, DmlTarget written, ReadScope scope, FlowState state, SourceLocation at)
    {
        if (written.Direct is { IsVariable: true } variable)
        {
            if (names.TableVariable(variable)?.Columns is not { } declared || !TracedColumns(state, variable, declared).Any())
                return state;
            var into = insert.Source is { IsDefaultValues: true } ? [] : TableVariableTargets(variable, insert.Columns);
            var values = into is null ? null : InsertedRows(insert.Source, into.Count, at, scope);
            return TableVariableWritten(state, variable, into, values, values is null ? null : InsertedExpressions(insert.Source, values.Count), at);
        }

        if (written.Direct is not { IsTemporary: true } name || state.Temp(name) is not KnownTable table)
            return state;
        var targets = InsertTargets(table, insert.Columns, insert.Source);
        if (targets.Any(i => i < 0))
            return state.WithTemp(name, Unknown(name, Unsupported(IrUnknownReason.UnresolvedColumn, "The merge's insert names a column the table does not have.", at)));
        var rows = InsertedRows(insert.Source, targets.Count, at, scope);
        return rows is null
            ? state.WithTemp(name, Unknown(name, Unsupported(IrUnknownReason.UnsupportedStatement, "The merge's insert values do not match its columns one to one.", at)))
            : state.WithTemp(name, Written(table, targets, rows, at, InsertedExpressions(insert.Source, rows.Count)));
    }

    /// <summary>
    /// An UPDATE or a MERGE whose values can't be read (a WITH that isn't modelled): the temporary table it writes is unknown, a
    /// table variable's traced columns are, and each variable its SET clauses set may be set to a value not known (observed:
    /// An_update_under_with_xmlnamespaces_sets_its_variables).
    /// </summary>
    /// <param name="clauses">Its SET clauses: an UPDATE's, or those of a MERGE's UPDATE actions.</param>
    private FlowState Unread(IEnumerable<SetClause> clauses, LocalName? target, UnknownShape why, FlowState state, SourceLocation at)
    {
        if (target is { IsTemporary: true } temporary)
            state = state.WithTemp(temporary, Unknown(temporary, why));
        else if (target is { IsVariable: true } variable)
            state = TableVariableWritten(state, variable, null, null, null, at);
        foreach (var assignment in clauses.OfType<AssignmentSetClause>().Where(a => a.Variable is not null))
            state = Assign(state, Resolved(assignment.Variable).Name, Value.Unknown(why), definitely: false, At(assignment));
        return state;
    }
}
