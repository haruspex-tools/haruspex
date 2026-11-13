using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using static Haruspex.SqlServer.Inference.Typer;

namespace Haruspex.SqlServer.Inference;

// What statements do to the body's own tables: SELECT … INTO, INSERT, UPDATE and MERGE of a temporary table, and CREATE,
// ALTER and DROP of one. README.md in this folder ("The state") gives the rules and the observations they rest on
// (StatementFlowObservationTests in the differential tests).
internal sealed partial class Walker
{
    /// <summary>
    /// <c>SELECT … INTO #t</c>: a new table whose columns are the select's, each holding what the select gives, with the
    /// facts and origins of a value that passes through unchanged. JSON is text once stored (observed), unless its SQL type
    /// is json, which is not tracked, so live JSON stored here is unknown.
    /// </summary>
    private FlowState SelectInto(SelectStatement select, FlowState state)
    {
        var at = At(select);
        if (Binder.Temporary(select.Into) is not { } name)
        {
            Blocked(select);
            return state;
        }

        var table = typer.WithCtes<TempTable>(select.WithCtesAndXmlNamespaces, Reading(state), scope =>
        {
            var row = typer.Outermost(select.QueryExpression, scope);
            if (row.Star is { } star)
                return Unknown(name, Unsupported(IrUnknownReason.UnsupportedQuery, "SELECT * is not expanded yet.", star));
            if (row.Columns.Any(c => string.IsNullOrEmpty(c.Name)) || row.Columns.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                return Unknown(name, Unsupported(IrUnknownReason.UnnamedColumn, $"SELECT … INTO {name} has a column without a name, or two of one name, which SQL Server refuses.", at));
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
        var bound = query.SelectElements.OfType<SelectScalarExpression>()
            .Select(e => e.Expression is ColumnReferenceExpression column ? binder.Resolve(column, scope) : null)
            .ToList();
        var elements = query.SelectElements.OfType<SelectScalarExpression>().ToList();
        return [.. elements.Select((element, i) =>
        {
            if (bound[i] is not { } binding)
                return element.Expression is ParenthesisExpression ? null : false;
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
    private static Value Stored(Value value, string column, SourceLocation at, ScalarExpression? expression = null) => value.IsLiveJson
        ? expression is not null && BuiltAsText(expression) && value.Shape is not UnknownShape
            ? new Value(new JsonTextShape(value.Shape), value.NullableBecause)
            : Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"JSON stored in {column} is text unless its SQL type is json, which is not tracked yet.", at))
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

        // DEFAULT VALUES writes no column: each gets its default, or null, and a column SELECT … INTO made has no default.
        var targets = spec.InsertSource is ValuesInsertSource { IsDefaultValues: true } ? []
            : spec.Columns.Count == 0
            ? [.. Enumerable.Range(0, table.Columns.Count)]
            : Binder.Written(spec.Columns, [.. table.Columns.Select(c => c.Name)]).ToList();
        if (targets.Any(i => i < 0))
            return state.WithTemp(name, Unknown(name, Unsupported(IrUnknownReason.UnresolvedColumn, "The insert names a column the table does not have.", at)));

        var rows = typer.WithCtes(insert.WithCtesAndXmlNamespaces, Reading(state), scope => InsertedRows(spec.InsertSource, targets.Count, at, scope), why => [[.. targets.Select(_ => Value.Unknown(why))]]);
        if (rows is null)
            return state.WithTemp(name, Unknown(name, Unsupported(IrUnknownReason.UnsupportedStatement, "The insert's values do not match its columns one to one.", at)));

        return state.WithTemp(name, Written(table, targets, rows, at, InsertedExpressions(spec.InsertSource, rows.Count)));
    }

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
                if (row.Star is { } star)
                    return [[.. Enumerable.Range(0, count).Select(_ => Value.Unknown(Unsupported(IrUnknownReason.UnsupportedQuery, "SELECT * is not expanded yet.", star)))]];
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
        var at = At(update);
        var target = written.Direct;

        return typer.WithCtes(update.WithCtesAndXmlNamespaces, Reading(state), statement =>
        {
            var scope = typer.Attach(statement.In(binder.BindUpdate(spec, statement.Names).Scope));
            var table = target is { IsTemporary: true } temporary ? state.Temp(temporary) as KnownTable : null;
            var columns = table?.Columns.ToList();
            foreach (var clause in spec.SetClauses)
            {
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
        }, why => target is { IsTemporary: true } temporary ? state.WithTemp(temporary, Unknown(temporary, why)) : state);
    }

    /// <summary>MERGE into a temporary table is not modelled, so the table becomes unknown; a table variable is as declared.</summary>
    private FlowState Merge(MergeStatement merge, DmlTarget target, FlowState state) =>
        target.Direct is { IsTemporary: true } name && state.Temp(name) is KnownTable
            ? state.WithTemp(name, Unknown(name, Unsupported(IrUnknownReason.UnsupportedStatement, $"MERGE at {At(merge)} is not modelled yet.", At(merge))))
            : target.Direct is { IsVariable: true } variable ? TableVariableWritten(state, variable, null, null, null, At(merge))
            : state;

    // ---- creating and altering temporary tables -----------------------------------------------------------------

    /// <summary>
    /// <c>CREATE TABLE #t</c>. It always makes a new table: one of the caller's of the same name is hidden (observed). A
    /// procedure cannot create one name twice, even after dropping it (error 2714 when the procedure is created, observed),
    /// so re-creation never reaches a procedure that runs; it is modelled as the new table all the same.
    /// </summary>
    /// <param name="create">The statement.</param>
    /// <param name="name">The temporary table it creates (<see cref="Binder.Temporary"/>).</param>
    private KnownTable Created(CreateTableStatement create, LocalName name)
    {
        var definition = SyntaxReader.Declared(name.Text, create.Definition, source.Within(create));
        return new KnownTable(name, [.. definition.Columns.Select(c => Fresh(definition, c, name, c.IsIdentity, null))]);
    }

    /// <summary>
    /// A declared column of a temporary table, as created: of <c>nvarchar(max)</c>, it is traced for JSON, holding none
    /// yet, unless its rows were given values at <paramref name="filled"/> (a column added with a default, or one whose type
    /// changed), which are not read.
    /// </summary>
    private TempColumn Fresh(DeclaredTable definition, Column column, LocalName name, bool? identity, SourceLocation? filled)
    {
        var value = typer.DeclaredValue(definition, column, name.Text, temporary: true);
        var json = !Traced(value) || column.IsComputed ? null
            : filled is { } at ? JsonSlot.Text(Unsupported(IrUnknownReason.JsonShapeUnknown, $"{name}.{column.Name} is given values {Text(at)} that are not read.", at))
            : JsonSlot.Empty;
        return new TempColumn(column.Name, value, definition, identity, json);
    }

    private FlowState Alter(AlterTableStatement alter, LocalName name, FlowState state)
    {
        var at = At(alter);
        if (state.Temp(name) is not KnownTable table)
            return state;

        switch (alter)
        {
            case AlterTableAddTableElementStatement add:
            {
                // A new column's declaration is read like CREATE TABLE's. Constraints added on existing columns are not
                // read, which only claims less. WITH NOCHECK leaves the new columns' own checks unproved for the rows there.
                var definition = SyntaxReader.Declared(name.Text, add.Definition, source.Within(alter));
                if (add.ExistingRowsCheckEnforcement == ConstraintEnforcement.NoCheck)
                    definition = Disabled(definition, null, at);
                return state.WithTemp(name, table with
                {
                    Columns = [.. table.Columns, .. definition.Columns.Select(c => Fresh(definition, c, name, c.IsIdentity, add.Definition.ColumnDefinitions
                        .Any(d => d.ColumnIdentifier.Value.Equals(c.Name, StringComparison.OrdinalIgnoreCase) && d.DefaultConstraint is not null) ? at : null))],
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
                    SyntaxReader.Type(change.DataType),
                    change.AlterTableAlterColumnOption == AlterTableAlterColumnOption.NotNull ? DeclaredNullability.NotNull : DeclaredNullability.Null,
                    IsIdentity: false,
                    ComputedExpression: null,
                    at,
                    change.Collation?.Value);
                var definition = new DeclaredTable(name.Text, source.Within(alter), [column], [], [], []);
                var columns = table.Columns.ToList();
                columns[i] = Fresh(definition, column, name, table.Columns[i].Identity, at);
                return state.WithTemp(name, table with { Columns = [.. columns] });
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
                            return state.WithTemp(name, Unknown(name, Unsupported(IrUnknownReason.UnsupportedStatement, $"{Text(alter)} is not modelled yet.", at)));
                    }
                }

                return state.WithTemp(name, table with { Columns = [.. columns] });
            }

            case AlterTableConstraintModificationStatement modify when modify.ConstraintEnforcement == ConstraintEnforcement.NoCheck:
            {
                // NOCHECK CONSTRAINT disables a check, which then checks no row (see CheckReader). CHECK CONSTRAINT enables
                // it again, but is not read, which only claims less.
                var columns = table.Columns.ToList();
                if (modify.All)
                    return state.WithTemp(name, table with { Columns = [.. Rechecked(columns, null, name, at, drop: false)] });
                foreach (var constraint in modify.ConstraintNames)
                    columns = Rechecked(columns, constraint.Value, name, at, drop: false);
                return state.WithTemp(name, table with { Columns = [.. columns] });
            }

            case AlterTableConstraintModificationStatement:
                return state;
        }

        return state.WithTemp(name, Unknown(name, Unsupported(IrUnknownReason.UnsupportedStatement, $"{Text(alter)} is not modelled yet.", at)));
    }

    /// <summary>
    /// The columns again, each declared one read with the check named <paramref name="constraint"/> (every check, when null,
    /// or when no check has that name, since it may be one SQL Server named) disabled or dropped.
    /// </summary>
    private List<TempColumn> Rechecked(List<TempColumn> columns, string? constraint, LocalName name, SourceLocation at, bool drop)
    {
        var named = constraint is not null && columns.Any(c => c.Declaration?.Checks.Any(k => constraint.Equals(k.Name, StringComparison.OrdinalIgnoreCase)) == true);
        return [.. columns.Select(c =>
        {
            if (c.Declaration is not { } declaration)
                return c;
            var changed = drop
                ? declaration with { Checks = [.. declaration.Checks.Where(k => !Matches(k))] }
                : Disabled(declaration, k => Matches(k), at);
            // Never null: the column is its own declaration's.
            return c with { Value = typer.DeclaredValue(changed, changed.Column(c.Name)!, name.Text, temporary: true), Declaration = changed };
        })];

        bool Matches(CheckConstraint check) => !named || (constraint is not null && constraint.Equals(check.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The table with the checks a statement may have disabled marked so. The copy keeps no unique keys: a key is not followed
    /// past a change to its table's constraints, which only claims less.
    /// </summary>
    private static DeclaredTable Disabled(DeclaredTable table, Func<CheckConstraint, bool>? which, SourceLocation at) =>
        table with { Checks = [.. table.Checks.Select(k => which is null || which(k) ? k with { DisabledAt = at } : k)], UniqueKeys = [] };
}
