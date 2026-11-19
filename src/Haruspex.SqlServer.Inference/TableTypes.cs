using Haruspex.Collections;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Syntax;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// What a table-valued parameter takes: its table type's definition as the IR carries it (<see cref="IrTableType"/>), and the
/// shape of what a client may send, an array of the type's rows, each with every column the type declares.
/// </summary>
/// <remarks>
/// <para>
/// What the server does with a row was observed through the mssql driver (<c>ParameterObservationTests</c>): the columns are
/// matched by place, not name, so every column is sent and in the type's order; a column can't be left out, even one with a
/// default, which a row sent never takes (error 500 for a missing column, 515 for NULL in a <c>NOT NULL</c> column with a
/// default); and a type with an identity, computed or <c>rowversion</c> column can't be sent at all, not even with no rows
/// (1077, 271, 273), only left out. So such a parameter is unknown, with that reason, rather than an array no call could send.
/// </para>
/// <para>
/// A column's type carries the facts its declaration and the type's CHECK constraints prove, as a scalar parameter's carries
/// its declared type's (<c>docs/decisions/0054-a-parameter-is-checked-against-its-declared-type.md</c>): the server refuses a
/// row that breaks a CHECK (547) and converts a value as it converts a scalar parameter's. Why each choice:
/// <c>docs/decisions/0076-a-table-valued-parameter-is-an-array-of-strict-rows-of-its-type.md</c>.
/// </para>
/// </remarks>
internal static class TableTypes
{
    /// <summary>The parameter's shape and its type's definition, for a table-valued parameter whose type the model defines; null otherwise.</summary>
    public static ParameterShape? Parameter(DatabaseModel model, Parameter parameter)
    {
        if (!parameter.IsReadOnly || SchemaRules.ResolveTableType(model, parameter.Type) is not { } type)
            return null;
        var definition = Definition(model, type);
        return new ParameterShape(parameter.Name, Rows(type, definition, parameter.At), HasDefault: true, IsOutput: false)
        {
            TableType = (type.Name.ToString(), definition),
        };
    }

    /// <summary>The type's definition as the IR carries it.</summary>
    public static IrTableType Definition(DatabaseModel model, TableType type)
    {
        var columns = type.Columns.Select(c => new IrTableColumn(c.Name, Spelt(model, c), !SchemaRules.IsNeverNull(model, type, c))
        {
            Type = c.IsComputed ? null : SqlTypes.ToShape(SchemaRules.ColumnFacts(model, type, c), c.Type, c.At) is ScalarShape scalar ? scalar.Type : null,
            Generated = Generation(c),
            Collation = SchemaRules.ResolveType(model, c.Type)?.Scalar is { IsString: true } ? SchemaRules.EffectiveCollation(model, type, c) : null,
        });
        return new IrTableType(type.Name.Schema, type.Name.Name, [.. columns], type.Span.Start.ToString())
        {
            Keys = [.. SchemaRules.EnforcedKeys(type).Select(k => new IrTableKey(k.Columns) { IgnoresDuplicates = k.IgnoresDuplicates })],
            Checks = [.. type.Checks.Select(c => FragmentText.Normalised(c.Expression))],
        };
    }

    /// <summary>
    /// What a client may send: an array of rows, or unknown, saying why, when some column is one a row can't give, or of a type
    /// a client can't be told how to send.
    /// </summary>
    private static Shape Rows(TableType type, IrTableType definition, Diagnostics.SourceLocation at)
    {
        var fields = new List<FieldShape>();
        foreach (var column in definition.Columns)
        {
            var why = column.Generated switch
            {
                IrColumnGeneration.Identity => $"an identity column, {column.Name}, so no row can be sent in it (error 1077, even for none); the parameter can only be left out",
                IrColumnGeneration.Computed => $"a computed column, {column.Name}, so no row can be sent in it (error 271, even for none); the parameter can only be left out",
                IrColumnGeneration.Rowversion => $"a rowversion column, {column.Name}, so no row can be sent in it (error 273, even for none); the parameter can only be left out",
                null when column.Type is null => $"its column {column.Name}, of the type {column.Sql ?? "(none)"}, which is not modelled yet, so a row of it isn't typed",
                null => null,
            };
            if (why is not null || column.Type is not { } scalar)
                return new UnknownShape(IrUnknownReason.UnsupportedSqlType, $"The table type {type.Name} has {why}.", at);

            // Every column is sent, so every key is there; a nullable column is sent as NULL, never left to its default.
            EquatableArray<IrNullReason> nullable = column.Nullable ? [new IrNullReason(IrNullRule.NullableColumn, $"{type.Name}.{column.Name}")] : [];
            fields.Add(new FieldShape(column.Name, ScalarShape.FromIr(scalar), IrPresence.Always, nullable));
        }

        return new ArrayShape(new TableRowShape(type.Name, new ObjectShape([.. fields])), []);
    }

    /// <summary>A column's type as the database spells it, its defaults written out (<c>decimal(18,0)</c>); null for a computed column.</summary>
    private static string? Spelt(DatabaseModel model, Column column) =>
        column.IsComputed ? null : SchemaRules.ResolveType(model, column.Type)?.Spelt ?? column.Type?.ToString();

    /// <summary>How the server fills a column a row can't give, or null.</summary>
    private static IrColumnGeneration? Generation(Column column) =>
        column.IsComputed ? IrColumnGeneration.Computed
        : column.IsIdentity ? IrColumnGeneration.Identity
        : column.Type is { Kind: SqlTypeKind.BuiltIn, Name: "rowversion" or "timestamp" } ? IrColumnGeneration.Rowversion
        : null;
}
