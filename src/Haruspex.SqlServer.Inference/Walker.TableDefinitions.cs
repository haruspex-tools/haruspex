using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using static Haruspex.SqlServer.Inference.Typer;

namespace Haruspex.SqlServer.Inference;

// What CREATE TABLE #t and ALTER TABLE #t do to a body's temporary table: its declared columns, their CHECK constraints, and
// the forms of ALTER TABLE that change them. Writes to it are Walker.Tables.cs. README.md in this folder ("The state") gives
// the rules and the observations they rest on (StatementFlowObservationTests in the differential tests).
internal sealed partial class Walker
{
    /// <summary>
    /// <c>CREATE TABLE #t</c>. It always makes a new table: one of the caller's of the same name is hidden (observed). A
    /// procedure cannot create one name twice, even after dropping it (error 2714 when the procedure is created, observed),
    /// so re-creation never reaches a procedure that runs; it is modelled as the new table all the same.
    /// </summary>
    /// <remarks>
    /// A table with a column set is not modelled: a star over it gives the set in place of its sparse columns (observed, for a
    /// temporary table and a table variable: A_column_set_stands_for_its_sparse_columns_in_star), which the columns the walk
    /// passes to binding can't say.
    /// </remarks>
    /// <param name="create">The statement.</param>
    /// <param name="name">The temporary table it creates (<see cref="Binder.Temporary"/>).</param>
    private TempTable Created(CreateTableStatement create, LocalName name)
    {
        // CREATE TABLE #t … AS SELECT is made from its query as SELECT … INTO is (MadeFrom); AS EDGE fails when it runs
        // (A_temporary_table_cannot_be_a_graph_table), and lists no columns.
        if (create.Definition is null)
            return Unknown(name, Unsupported(IrUnknownReason.UnsupportedSource, "a temporary table created without a column list is not modelled yet.", At(create)));

        // Its keys are the ones it declares: the primary key, unique constraints and unfiltered unique indexes (INDEX … UNIQUE)
        // (A_temporary_table_enforces_the_keys_its_create_table_declares). An identity column is none.
        var definition = SyntaxReader.Declared(name.Text, create.Definition, source.Within(create));
        return definition.Columns.Any(c => c.IsColumnSet)
            ? WithColumnSet(name, At(create))
            : new KnownTable(name, [.. definition.Columns.Select(c => Fresh(definition, c, name, c.IsIdentity, null))]) { Keys = definition.UniqueKeys };
    }

    private static UnknownTable WithColumnSet(LocalName name, SourceLocation at) =>
        Unknown(name, Unsupported(IrUnknownReason.UnsupportedSource, "a temporary table with a column set is not modelled yet.", at));

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
                // A new column's declaration is read like CREATE TABLE's. CHECK constraints added on existing columns are not
                // read, which only claims less. WITH NOCHECK leaves the new columns' own checks unproved for the rows there, but
                // not a key, which rows already there must keep or the statement fails (Added).
                var definition = SyntaxReader.Declared(name.Text, add.Definition, source.Within(alter));
                if (definition.Columns.Any(c => c.IsColumnSet))
                    return state.WithTemp(name, WithColumnSet(name, at));
                var keys = definition.UniqueKeys;
                if (add.ExistingRowsCheckEnforcement == ConstraintEnforcement.NoCheck)
                    definition = Disabled(definition, null, at);
                var widened = table with
                {
                    Columns = [.. table.Columns, .. definition.Columns.Select(c => Fresh(definition, c, name, c.IsIdentity, add.Definition.ColumnDefinitions
                        .Any(d => d.ColumnIdentifier.Value.Equals(c.Name, StringComparison.OrdinalIgnoreCase) && d.DefaultConstraint is not null) ? at : null))],
                };
                return state.WithTemp(name, keys.Aggregate(widened, Added));
            }

            case AlterTableAlterColumnStatement change when change.DataType is not null
                && change.AlterTableAlterColumnOption is AlterTableAlterColumnOption.NoOptionDefined or AlterTableAlterColumnOption.Null or AlterTableAlterColumnOption.NotNull:
            {
                // The column takes the new type; without NOT NULL it may be null. Its checks and the keys over it are left out,
                // which claims less (SQL Server refuses to change a key's column, 5074).
                var i = table.IndexOf(change.ColumnIdentifier.Value);
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
                return state.WithTemp(name, table.WithoutKeysOver(column.Name) with { Columns = [.. columns] });
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
                            table = table.WithoutKeysOver(element.Name.Value);
                            break;
                        case TableElementType.Constraint or TableElementType.NotSpecified:
                            // A dropped CHECK proves nothing more, and a dropped key holds no longer. One of an unknown name
                            // may be a check without one, or a key SQL Server named.
                            columns = Rechecked(columns, element.Name.Value, name, at, drop: true);
                            table = table.Dropped(element.Name.Value);
                            break;
                        case TableElementType.Index:
                            table = table.Dropped(element.Name.Value);
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
    /// The table with a key an <c>ALTER TABLE … ADD</c> declares, over columns it has. A primary key's columns refuse NULL from
    /// then on: SQL Server adds one only over columns that already do (8111), which a column <c>SELECT … INTO</c> made may,
    /// whatever haruspex can tell of its rows. The key holds from this statement on, for every path that goes on past it: one that
    /// rows already break fails the statement (1505), which fails the call or reaches <c>CATCH</c> from the state before it
    /// (A_key_that_fails_to_be_added_fails_the_call_or_reaches_catch_without_it).
    /// </summary>
    private static KnownTable Added(KnownTable table, UniqueKey key)
    {
        if (!key.IsPrimaryKey)
            return table.WithKey(key);
        var columns = table.Columns.Select(c => key.Columns.Contains(c.Name, StringComparer.OrdinalIgnoreCase) ? c with { RefusesNull = true } : c);
        return (table with { Columns = [.. columns] }).WithKey(key);
    }

    /// <summary>
    /// <c>CREATE INDEX … ON #t</c>: a unique one without a filter is a key from here on, as one added by <c>ALTER TABLE</c> is
    /// (<see cref="Added"/>; A_key_added_to_a_temporary_table_holds_from_that_statement_on); one created <c>WITH (DROP_EXISTING =
    /// ON)</c> replaces the index of its name, which may have been unique.
    /// </summary>
    private FlowState IndexCreated(CreateIndexStatement index, LocalName name, FlowState state)
    {
        if (state.Temp(name) is not KnownTable table)
            return state;
        if (SyntaxReader.DropsExisting(index))
            table = table.Dropped(index.Name.Value);
        if (index.Unique && index.FilterPredicate is null)
            table = Added(table, SyntaxReader.Key(index, source.Within(index)));
        return state.WithTemp(name, table);
    }

    /// <summary>
    /// <c>ALTER INDEX … ON #t</c>: <c>DISABLE</c> stops a key being enforced, and <c>REBUILD</c> enables it again
    /// (A_temporary_table_s_key_holds_until_it_is_dropped_or_disabled); any other form leaves it unique.
    /// </summary>
    private static FlowState IndexAltered(AlterIndexStatement alter, LocalName name, FlowState state, SourceLocation at)
    {
        if (state.Temp(name) is not KnownTable table)
            return state;
        var index = alter.All ? null : alter.Name?.Value;
        return alter.AlterIndexType switch
        {
            AlterIndexType.Disable => state.WithTemp(name, table.Disabled(index, at)),
            AlterIndexType.Rebuild => state.WithTemp(name, table.Rebuilt(index)),
            _ => state,
        };
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
