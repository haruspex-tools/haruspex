using Haruspex.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Model;

/// <summary>A table: its columns as declared, and its primary key.</summary>
/// <remarks>
/// The model holds what the source declares and nothing it implies. Whether a column can be null, for example,
/// follows from its declaration, its key and its identity together; that interpretation belongs to whoever reads
/// the model (in haruspex, Haruspex.SqlServer.Semantics), not to the model.
/// </remarks>
/// <param name="name">Its name.</param>
/// <param name="columns">Its columns, in declaration order.</param>
/// <param name="primaryKey">The names of its primary key's columns, or none.</param>
/// <param name="at">Where it is defined.</param>
/// <param name="checks">Its CHECK constraints, on the table or on a column, in declaration order.</param>
/// <param name="uniqueKeys">Its primary key and unique constraints, and unique indexes without a filter declared with it.</param>
public sealed class Table(ObjectName name, IReadOnlyList<Column> columns, IReadOnlyList<string> primaryKey, SourceLocation at, IReadOnlyList<CheckConstraint>? checks = null, IReadOnlyList<UniqueKey>? uniqueKeys = null)
{
    /// <summary>
    /// Reads a table definition as written: in a <c>CREATE TABLE</c>, and equally in a body's <c>CREATE TABLE #t</c>,
    /// <c>DECLARE @t TABLE</c> or <c>ALTER TABLE … ADD</c>, which share ScriptDom's <see cref="TableDefinition"/>.
    /// </summary>
    /// <param name="name">The name the table is known by.</param>
    /// <param name="definition">Its columns and constraints.</param>
    /// <param name="file">The file it is in, for locations.</param>
    /// <param name="at">Where the table is defined.</param>
    /// <param name="isGraph">Whether it is declared <c>AS NODE</c> or <c>AS EDGE</c> (see <see cref="IsGraph"/>).</param>
    public static Table FromDefinition(ObjectName name, TableDefinition definition, string file, SourceLocation at, bool isGraph = false)
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
                SqlType.From(column.DataType),
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

    /// <summary>The CHECK constraints, whether declared on the table or on a column, in declaration order.</summary>
    public IReadOnlyList<CheckConstraint> Checks { get; } = checks ?? [];

    /// <summary>The table's name.</summary>
    public ObjectName Name { get; } = name;

    /// <summary>The columns, in declaration order.</summary>
    public IReadOnlyList<Column> Columns { get; } = columns ?? throw new ArgumentNullException(nameof(columns));

    /// <summary>
    /// The names of the primary key's columns, in key order, whether it is declared on the table or on a
    /// column. Empty when there is none.
    /// </summary>
    public IReadOnlyList<string> PrimaryKey { get; } = primaryKey ?? throw new ArgumentNullException(nameof(primaryKey));

    /// <summary>Where the table is defined.</summary>
    public SourceLocation At { get; } = at ?? throw new ArgumentNullException(nameof(at));

    /// <summary>
    /// Its primary key, unique constraints and unique indexes, each a set of columns no two rows share (SQL Server counts
    /// NULLs as equal there, so one NULL at most). Only what the table's definition declares; see <see cref="UniqueKey.DisabledAt"/>.
    /// </summary>
    public IReadOnlyList<UniqueKey> UniqueKeys { get; } = uniqueKeys ?? [];

    /// <summary>
    /// Whether it is a graph table (<c>AS NODE</c> or <c>AS EDGE</c>), whose <c>SELECT *</c> includes pseudo-columns the
    /// definition does not list.
    /// </summary>
    public bool IsGraph { get; init; }

    /// <summary>The column with this name, ignoring case as SQL Server does, or null.</summary>
    public Column? Column(string name) =>
        Columns.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether the column is part of the primary key, ignoring case.</summary>
    public bool IsInPrimaryKey(string column) => PrimaryKey.Contains(column, StringComparer.OrdinalIgnoreCase);

    /// <summary>The table's name.</summary>
    public override string ToString() => Name.ToString();
}
