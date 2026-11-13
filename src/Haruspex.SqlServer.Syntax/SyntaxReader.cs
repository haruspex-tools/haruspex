using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Syntax;

/// <summary>
/// Reads the declarations ScriptDom parsed into the model's values: a name, a type, a table definition. Sources reads a
/// script's <c>CREATE TABLE</c> with it, and Inference a body's <c>CREATE TABLE #t</c> and <c>DECLARE @t TABLE</c>, so a
/// declaration means the same wherever it is written.
/// </summary>
public static class SyntaxReader
{
    /// <summary>
    /// The name a definition creates. An unqualified name is taken as <c>dbo</c>, the default schema a database project
    /// deploys with. A reference in a body is resolved differently, because there the caller's default schema decides (see
    /// <c>Binder</c> in Haruspex.SqlServer.Semantics).
    /// </summary>
    /// <param name="name">The name as ScriptDom parsed it. Server and database parts are ignored.</param>
    public static ObjectName Name(SchemaObjectName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new(name.SchemaIdentifier?.Value ?? "dbo", name.BaseIdentifier.Value);
    }

    /// <summary>The type a data type reference declares, or null when there is none or it has no name.</summary>
    public static SqlType? Type(DataTypeReference? type)
    {
        switch (type)
        {
            case SqlDataTypeReference sql:
#pragma warning disable CA1308 // A canonical name, as sys.types gives it, not a comparison.
                return new SqlType(SqlTypeKind.BuiltIn, sql.SqlDataTypeOption.ToString().ToLowerInvariant(), [.. sql.Parameters.Select(p => p.Value)]);
#pragma warning restore CA1308
            case XmlDataTypeReference { Name.BaseIdentifier.Value: { } xml }:
                return new SqlType(SqlTypeKind.Xml, xml, []);
            case XmlDataTypeReference:
                return null;
            case { Name.BaseIdentifier.Value: { } name }:
                var arguments = (type as ParameterizedDataTypeReference)?.Parameters.Select(p => p.Value) ?? [];
                return new SqlType(SqlTypeKind.Named, name, [.. arguments], type.Name.SchemaIdentifier?.Value);
            default:
                return null;
        }
    }

    /// <summary>
    /// Reads a table definition as written: in a <c>CREATE TABLE</c>, and equally in a body's <c>CREATE TABLE #t</c>,
    /// <c>DECLARE @t TABLE</c> or <c>ALTER TABLE … ADD</c>, which share ScriptDom's <see cref="TableDefinition"/>.
    /// </summary>
    /// <param name="name">The name the table is known by.</param>
    /// <param name="definition">Its columns and constraints.</param>
    /// <param name="file">The file it is in, for locations.</param>
    /// <param name="at">Where the table is defined.</param>
    /// <param name="isGraph">Whether it is declared <c>AS NODE</c> or <c>AS EDGE</c> (see <see cref="Table.IsGraph"/>).</param>
    public static Table Table(ObjectName name, TableDefinition definition, string file, SourceLocation at, bool isGraph = false)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // Declared on the table, or on a column. SQL Server refuses a table with two primary keys, so collecting
        // every one only matters for a script that would not deploy.
        var primaryKey = definition.TableConstraints
            .OfType<UniqueConstraintDefinition>()
            .Where(u => u.IsPrimaryKey)
            .SelectMany(u => u.Columns)
            .Select(c => c.Column.MultiPartIdentifier.Identifiers[^1].Value)
            .Concat(definition.ColumnDefinitions
                .Where(c => c.Constraints.OfType<UniqueConstraintDefinition>().Any(u => u.IsPrimaryKey))
                .Select(c => c.ColumnIdentifier.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var columns = new List<Column>();
        foreach (var column in definition.ColumnDefinitions)
        {
            var nullable = column.Constraints.OfType<NullableConstraintDefinition>().ToList();

            // A column that says both NULL and NOT NULL is recorded as NOT NULL: inference read it so before the model
            // was split out of it, and the split was to change no behaviour.
            var nullability = nullable.Any(n => !n.Nullable) ? DeclaredNullability.NotNull
                : nullable.Count > 0 ? DeclaredNullability.Null
                : DeclaredNullability.NotDeclared;

            columns.Add(new Column(
                column.ColumnIdentifier.Value,
                Type(column.DataType),
                nullability,
                isIdentity: column.IdentityOptions is not null,
                column.ComputedColumnExpression,
                new SourceLocation(file, column.StartLine, column.StartColumn),
                column.Collation?.Value)
            {
                IsHidden = column.IsHidden,
            });
        }

        // Column constraints first, then table constraints, each in declaration order. ALTER TABLE … ADD CONSTRAINT
        // is not read (see OnlyTightens in Sources): a check added later is left out, which only claims less.
        var checks = definition.ColumnDefinitions
            .SelectMany(c => c.Constraints.OfType<CheckConstraintDefinition>().Select(k => Check(k, c.ColumnIdentifier.Value, file)))
            .Concat(definition.TableConstraints.OfType<CheckConstraintDefinition>().Select(k => Check(k, null, file)))
            .ToList();

        // Unique constraints and the primary key, on the table or on a column, and unique indexes declared in the table
        // (INDEX … UNIQUE) without a filter: a filtered index is unique only among the rows its filter keeps.
        // CREATE UNIQUE INDEX and ALTER TABLE … ADD CONSTRAINT are not read here, which only claims less.
        var keys = definition.ColumnDefinitions
            .SelectMany(c => c.Constraints.OfType<UniqueConstraintDefinition>().Select(u => new UniqueKey(
                u.ConstraintIdentifier?.Value, [c.ColumnIdentifier.Value], u.IsPrimaryKey, new SourceLocation(file, u.StartLine, u.StartColumn))))
            .Concat(definition.TableConstraints.OfType<UniqueConstraintDefinition>().Select(u => new UniqueKey(
                u.ConstraintIdentifier?.Value,
                [.. u.Columns.Select(c => c.Column.MultiPartIdentifier.Identifiers[^1].Value)],
                u.IsPrimaryKey,
                new SourceLocation(file, u.StartLine, u.StartColumn))))
            .Concat(definition.Indexes.Where(i => i.Unique && i.FilterPredicate is null).Select(i => new UniqueKey(
                i.Name?.Value,
                [.. i.Columns.Select(c => c.Column.MultiPartIdentifier.Identifiers[^1].Value)],
                false,
                new SourceLocation(file, i.StartLine, i.StartColumn))))
            .ToList();

        return new Table(name, columns, primaryKey, at, checks, keys) { IsGraph = isGraph };
    }

    private static CheckConstraint Check(CheckConstraintDefinition check, string? column, string file) =>
        new(check.ConstraintIdentifier?.Value, check.CheckCondition, column, check.NotForReplication, new SourceLocation(file, check.StartLine, check.StartColumn));
}
