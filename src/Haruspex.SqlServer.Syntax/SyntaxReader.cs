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
    /// deploys with. A reference in a body is resolved differently: in a module's schema and then <c>dbo</c>, or by the caller's
    /// default schema in a query file or an <c>EXEC</c> (see <c>SchemaSearch</c> in Haruspex.SqlServer.Semantics).
    /// </summary>
    /// <param name="name">The name as ScriptDom parsed it. Server and database parts are ignored.</param>
    public static ObjectName Name(SchemaObjectName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new(name.SchemaIdentifier?.Value ?? "dbo", name.BaseIdentifier.Value);
    }

    /// <summary>The type a data type reference declares, or null when there is none or it has no name.</summary>
    /// <param name="type">The reference as ScriptDom parsed it.</param>
    /// <param name="moduleSchema">
    /// The schema of the module whose definition it is written in, where SQL Server looks for a type named without a schema
    /// before <c>dbo</c> (<see cref="SqlType.ModuleSchema"/>); null where it is not in a module's definition.
    /// </param>
    public static SqlType? Type(DataTypeReference? type, string? moduleSchema = null)
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
                return type.Name.SchemaIdentifier?.Value is { } schema
                    ? new SqlType(SqlTypeKind.Named, name, [.. arguments], schema)
                    : new SqlType(SqlTypeKind.Named, name, [.. arguments], ModuleSchema: moduleSchema);
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
    /// <param name="moduleSchema">
    /// For a table variable or a function's return table, the schema of the module it is declared in, where its columns' types
    /// are looked for (<see cref="Type"/>); null for any other table.
    /// </param>
    public static Table Table(ObjectName name, TableDefinition definition, SourceSpan span, bool isGraph = false, string? moduleSchema = null)
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

            // SQL Server refuses a column that says both NULL and NOT NULL, in either order, wherever a table is declared
            // (observed: A_column_declared_both_null_and_not_null_is_refused, Msg 8150), so how one is read only matters for a
            // script that would not deploy, and no value is ever read from such a column. It is recorded as NOT NULL.
            var nullability = nullable.Any(n => !n.Nullable) ? DeclaredNullability.NotNull
                : nullable.Count > 0 ? DeclaredNullability.Null
                : DeclaredNullability.NotDeclared;

            columns.Add(new Column(
                column.ColumnIdentifier.Value,
                Type(column.DataType, moduleSchema),
                nullability,
                IsIdentity: column.IdentityOptions is not null,
                column.ComputedColumnExpression,
                span.At(column),
                column.Collation?.Value)
            {
                IsHidden = column.IsHidden,
                IsSparse = column.StorageOptions?.SparseOption == SparseColumnOption.Sparse,
                IsColumnSet = column.StorageOptions?.SparseOption == SparseColumnOption.ColumnSetForAllSparseColumns,
                Mask = column.MaskingFunction?.Value,
            });
        }

        // Column constraints first, then table constraints, each in declaration order. ALTER TABLE … ADD CONSTRAINT
        // is not read (see OnlyTightens in Sources): a check added later is left out, which only claims less.
        var checks = definition.ColumnDefinitions
            .SelectMany(c => c.Constraints.OfType<CheckConstraintDefinition>().Select(k => Check(k, c.ColumnIdentifier.Value, span)))
            .Concat(definition.TableConstraints.OfType<CheckConstraintDefinition>().Select(k => Check(k, null, span)))
            .ToList();

        // Unique constraints and the primary key, on the table or on a column, and unique indexes declared in the table or on a
        // column (INDEX … UNIQUE) without a filter: a filtered index is unique only among the rows its filter keeps.
        // CREATE UNIQUE INDEX and ALTER TABLE … ADD CONSTRAINT, after the table, are read by the folder's reader.
        var keys = definition.ColumnDefinitions
            .SelectMany(c => c.Constraints.OfType<UniqueConstraintDefinition>().Select(u => ColumnKey(u, c.ColumnIdentifier.Value, span)))
            .Concat(definition.ColumnDefinitions.Where(c => c.Index is { Unique: true, FilterPredicate: null }).Select(c => IndexKey(c.Index, c.ColumnIdentifier.Value, span)))
            .Concat(definition.TableConstraints.OfType<UniqueConstraintDefinition>().Select(u => Key(u, span)))
            .Concat(definition.Indexes.Where(i => i.Unique && i.FilterPredicate is null).Select(i => IndexKey(i, null, span)))
            .ToList();

        return new Table(name, span, [.. columns], [.. primaryKey], [.. checks], [.. keys]) { IsGraph = isGraph };
    }

    /// <summary>A primary key or unique constraint declared on a table, of a <c>CREATE TABLE</c> or an <c>ALTER TABLE … ADD</c>.</summary>
    /// <param name="key">The constraint.</param>
    /// <param name="span">Where the statement that declares it is.</param>
    public static UniqueKey Key(UniqueConstraintDefinition key, SourceSpan span)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(span);
        return new UniqueKey(
            key.ConstraintIdentifier?.Value,
            [.. key.Columns.Select(c => c.Column.MultiPartIdentifier.Identifiers[^1].Value)],
            key.IsPrimaryKey,
            span.At(key))
        {
            IgnoresDuplicates = IgnoresDuplicates(key.IndexOptions),
        };
    }

    /// <summary>A primary key or unique constraint declared on a column.</summary>
    private static UniqueKey ColumnKey(UniqueConstraintDefinition key, string column, SourceSpan span) =>
        new(key.ConstraintIdentifier?.Value, [column], key.IsPrimaryKey, span.At(key)) { IgnoresDuplicates = IgnoresDuplicates(key.IndexOptions) };

    /// <summary>
    /// A unique index declared in the table (<c>INDEX … UNIQUE</c>), or on <paramref name="column"/> (<c>id int INDEX ux UNIQUE</c>,
    /// which lists no columns of its own; enforced as the other, observed: A_temporary_table_enforces_the_keys_its_create_table_declares).
    /// </summary>
    private static UniqueKey IndexKey(IndexDefinition index, string? column, SourceSpan span) =>
        new(index.Name?.Value, column is null ? [.. index.Columns.Select(c => c.Column.MultiPartIdentifier.Identifiers[^1].Value)] : [column], false, span.At(index))
        {
            IgnoresDuplicates = IgnoresDuplicates(index.IndexOptions),
        };

    /// <summary>
    /// The key a <c>CREATE UNIQUE INDEX</c> without a filter declares: its columns, in key order, whatever their sort order; the
    /// columns it includes are no part of it.
    /// </summary>
    /// <param name="index">The statement, which must be unique and unfiltered.</param>
    /// <param name="span">Where the statement is.</param>
    public static UniqueKey Key(CreateIndexStatement index, SourceSpan span)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(span);
        if (!index.Unique || index.FilterPredicate is not null)
            throw new ArgumentException("Only a unique index without a filter is a key.", nameof(index));
        return new UniqueKey(index.Name.Value, [.. index.Columns.Select(c => c.Column.MultiPartIdentifier.Identifiers[^1].Value)], false, span.At(index))
        {
            IgnoresDuplicates = IgnoresDuplicates(index.IndexOptions),
        };
    }

    /// <summary>Whether an index's options say <c>DROP_EXISTING = ON</c>: it replaces the index of its name, which may have been unique.</summary>
    /// <param name="index">The statement.</param>
    public static bool DropsExisting(CreateIndexStatement index)
    {
        ArgumentNullException.ThrowIfNull(index);
        return index.IndexOptions.OfType<IndexStateOption>().Any(o => o.OptionKind == IndexOptionKind.DropExisting && o.OptionState == OptionState.On);
    }

    /// <summary>
    /// The indexes a <c>DROP INDEX</c> drops, each with the table it names: <c>DROP INDEX ix ON t</c>, and the older
    /// <c>DROP INDEX t.ix</c> or <c>DROP INDEX schema.t.ix</c>, whose index is the name's last part.
    /// </summary>
    /// <param name="drop">The statement.</param>
    public static IEnumerable<(SchemaObjectName Table, string Index)> DroppedIndexes(DropIndexStatement drop)
    {
        ArgumentNullException.ThrowIfNull(drop);
        foreach (var clause in drop.DropIndexClauses)
        {
            switch (clause)
            {
                case DropIndexClause on:
                    yield return (on.Object, on.Index.Value);
                    break;
                case BackwardsCompatibleDropIndexClause old:
                    var table = new SchemaObjectName();
                    if (old.Index.SchemaIdentifier is { } schema)
                        table.Identifiers.Add(schema);
                    table.Identifiers.Add(old.Index.BaseIdentifier);
                    yield return (table, old.Index.ChildIdentifier.Value);
                    break;
            }
        }
    }

    /// <summary>Whether a key's or index's options say <c>IGNORE_DUP_KEY = ON</c> (<see cref="UniqueKey.IgnoresDuplicates"/>).</summary>
    private static bool IgnoresDuplicates(IEnumerable<IndexOption> options) =>
        options.OfType<IgnoreDupKeyIndexOption>().Any(o => o.OptionState == OptionState.On);

    /// <summary>
    /// Reads a table type's definition (<c>CREATE TYPE … AS TABLE</c>) as <see cref="Table(ObjectName, TableDefinition, SourceSpan, bool, string)"/>
    /// reads a table's: its columns, keys and CHECK constraints are declared in the same grammar.
    /// </summary>
    /// <param name="name">The type's name.</param>
    /// <param name="definition">Its columns and constraints.</param>
    /// <param name="span">Where the statement that defines it is.</param>
    public static TableType TableType(ObjectName name, TableDefinition definition, SourceSpan span)
    {
        var table = Table(name, definition, span);

        // Only a table type's columns record a default: a table's would be read past either way, and a package keeps a table's
        // defaults apart from it, where its reader doesn't look.
        var columns = table.Columns.Select((c, i) => c with { HasDefault = definition.ColumnDefinitions[i].DefaultConstraint is not null });
        return new TableType(name, table.Span, [.. columns], table.PrimaryKey, table.Checks, table.UniqueKeys);
    }

    /// <summary>
    /// Reads the table a body declares for itself (<c>CREATE TABLE #t</c>, <c>DECLARE @t TABLE</c>, <c>ALTER TABLE #t ADD</c>, a
    /// function's return table) as <see cref="Table(ObjectName, TableDefinition, SourceSpan, bool, string)"/> reads the schema's.
    /// </summary>
    /// <param name="name">The name the body gives it, with its sigil.</param>
    /// <param name="definition">Its columns and constraints.</param>
    /// <param name="span">Where the statement that declares it is.</param>
    /// <param name="moduleSchema">
    /// For a table variable or a function's return table, the schema of the module it is declared in (see <see cref="Type"/>);
    /// null for a temporary table, whose columns' types SQL Server looks for in tempdb.
    /// </param>
    public static DeclaredTable Declared(string name, TableDefinition definition, SourceSpan span, string? moduleSchema = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        var table = Table(new ObjectName("", name), definition, span, moduleSchema: moduleSchema);
        return new DeclaredTable(name, table.Span, table.Columns, table.PrimaryKey, table.Checks, table.UniqueKeys);
    }

    private static CheckConstraint Check(CheckConstraintDefinition check, string? column, SourceSpan span) =>
        new(check.ConstraintIdentifier?.Value, check.CheckCondition, column, check.NotForReplication, span.At(check));
}
