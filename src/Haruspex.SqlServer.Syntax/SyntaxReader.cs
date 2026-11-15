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

    /// <summary>Where a fragment is in <paramref name="file"/>: from its first token to the column after its last.</summary>
    /// <param name="file">The file, relative to the source root, with <c>/</c> separators.</param>
    /// <param name="fragment">What ScriptDom parsed.</param>
    public static SourceSpan Span(string file, TSqlFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        var last = fragment.ScriptTokenStream[fragment.LastTokenIndex];
        var (endLine, endColumn) = After(last.Line, last.Column, last.Text);
        return new SourceSpan(file, fragment.StartLine, fragment.StartColumn, endLine, endColumn);
    }

    /// <summary>Where a fragment inside an object is, in the object's file.</summary>
    /// <param name="within">The object's span.</param>
    /// <param name="fragment">A fragment of the object, such as a statement of its body.</param>
    public static SourceSpan Within(this SourceSpan within, TSqlFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(within);
        return Span(within.File, fragment);
    }

    /// <summary>Where a fragment inside an object starts, in the object's file: the point a diagnostic reports.</summary>
    /// <param name="within">The object's span.</param>
    /// <param name="fragment">A fragment of the object, such as a statement of its body.</param>
    public static SourceLocation At(this SourceSpan within, TSqlFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(within);
        ArgumentNullException.ThrowIfNull(fragment);
        return within.At(fragment.StartLine, fragment.StartColumn);
    }

    /// <summary>
    /// The position after a token's text, counting lines as ScriptDom does: CRLF, LF and a lone CR each end one. A
    /// token that spans lines is a string or a comment.
    /// </summary>
    private static (int Line, int Column) After(int line, int column, string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '\r' or '\n')
            {
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    /// <summary>
    /// Reads a table definition as written: in a <c>CREATE TABLE</c>, and equally in a body's <c>CREATE TABLE #t</c>,
    /// <c>DECLARE @t TABLE</c> or <c>ALTER TABLE … ADD</c>, which share ScriptDom's <see cref="TableDefinition"/>.
    /// </summary>
    /// <param name="name">The name the table is known by.</param>
    /// <param name="definition">Its columns and constraints.</param>
    /// <param name="span">Where the statement that defines it is; its columns and constraints are positions in its file.</param>
    /// <param name="isGraph">Whether it is declared <c>AS NODE</c> or <c>AS EDGE</c> (see <see cref="Table.IsGraph"/>).</param>
    public static Table Table(ObjectName name, TableDefinition definition, SourceSpan span, bool isGraph = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(span);

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
                IsIdentity: column.IdentityOptions is not null,
                column.ComputedColumnExpression,
                span.At(column),
                column.Collation?.Value)
            {
                IsHidden = column.IsHidden,
                IsSparse = column.StorageOptions?.SparseOption == SparseColumnOption.Sparse,
                IsColumnSet = column.StorageOptions?.SparseOption == SparseColumnOption.ColumnSetForAllSparseColumns,
            });
        }

        // Column constraints first, then table constraints, each in declaration order. ALTER TABLE … ADD CONSTRAINT
        // is not read (see OnlyTightens in Sources): a check added later is left out, which only claims less.
        var checks = definition.ColumnDefinitions
            .SelectMany(c => c.Constraints.OfType<CheckConstraintDefinition>().Select(k => Check(k, c.ColumnIdentifier.Value, span)))
            .Concat(definition.TableConstraints.OfType<CheckConstraintDefinition>().Select(k => Check(k, null, span)))
            .ToList();

        // Unique constraints and the primary key, on the table or on a column, and unique indexes declared in the table
        // (INDEX … UNIQUE) without a filter: a filtered index is unique only among the rows its filter keeps.
        // CREATE UNIQUE INDEX and ALTER TABLE … ADD CONSTRAINT are not read here, which only claims less.
        var keys = definition.ColumnDefinitions
            .SelectMany(c => c.Constraints.OfType<UniqueConstraintDefinition>().Select(u => new UniqueKey(
                u.ConstraintIdentifier?.Value, [c.ColumnIdentifier.Value], u.IsPrimaryKey, span.At(u))))
            .Concat(definition.TableConstraints.OfType<UniqueConstraintDefinition>().Select(u => new UniqueKey(
                u.ConstraintIdentifier?.Value,
                [.. u.Columns.Select(c => c.Column.MultiPartIdentifier.Identifiers[^1].Value)],
                u.IsPrimaryKey,
                span.At(u))))
            .Concat(definition.Indexes.Where(i => i.Unique && i.FilterPredicate is null).Select(i => new UniqueKey(
                i.Name?.Value,
                [.. i.Columns.Select(c => c.Column.MultiPartIdentifier.Identifiers[^1].Value)],
                false,
                span.At(i))))
            .ToList();

        return new Table(name, span, [.. columns], [.. primaryKey], [.. checks], [.. keys]) { IsGraph = isGraph };
    }

    /// <summary>
    /// Reads the table a body declares for itself (<c>CREATE TABLE #t</c>, <c>DECLARE @t TABLE</c>, <c>ALTER TABLE #t ADD</c>, a
    /// function's return table) as <see cref="Table(ObjectName, TableDefinition, SourceSpan, bool)"/> reads the schema's.
    /// </summary>
    /// <param name="name">The name the body gives it, with its sigil.</param>
    /// <param name="definition">Its columns and constraints.</param>
    /// <param name="span">Where the statement that declares it is.</param>
    public static DeclaredTable Declared(string name, TableDefinition definition, SourceSpan span)
    {
        ArgumentNullException.ThrowIfNull(name);
        var table = Table(new ObjectName("", name), definition, span);
        return new DeclaredTable(name, table.Span, table.Columns, table.PrimaryKey, table.Checks, table.UniqueKeys);
    }

    private static CheckConstraint Check(CheckConstraintDefinition check, string? column, SourceSpan span) =>
        new(check.ConstraintIdentifier?.Value, check.CheckCondition, column, check.NotForReplication, span.At(check));
}
