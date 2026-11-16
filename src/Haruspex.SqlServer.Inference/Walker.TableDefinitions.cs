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
        // CREATE TABLE #t … AS SELECT has its query's columns (observed: A_temporary_table_made_from_a_query_has_the_columns_the_query_gives),
        // not read here yet; AS EDGE fails when it runs (A_temporary_table_cannot_be_a_graph_table). Neither lists its columns.
        if (create.Definition is null)
            return Unknown(name, Unsupported(IrUnknownReason.UnsupportedSource, "a temporary table created without a column list is not modelled yet.", At(create)));

        var definition = SyntaxReader.Declared(name.Text, create.Definition, source.Within(create));
        return definition.Columns.Any(c => c.IsColumnSet)
            ? WithColumnSet(name, At(create))
            : new KnownTable(name, [.. definition.Columns.Select(c => Fresh(definition, c, name, c.IsIdentity, null))]);
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
                // A new column's declaration is read like CREATE TABLE's. Constraints added on existing columns are not
                // read, which only claims less. WITH NOCHECK leaves the new columns' own checks unproved for the rows there.
                var definition = SyntaxReader.Declared(name.Text, add.Definition, source.Within(alter));
                if (definition.Columns.Any(c => c.IsColumnSet))
                    return state.WithTemp(name, WithColumnSet(name, at));
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
