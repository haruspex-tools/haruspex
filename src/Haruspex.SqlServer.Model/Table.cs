using Haruspex.Diagnostics;

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
/// <param name="span">Where its definition is.</param>
/// <param name="checks">Its CHECK constraints, on the table or on a column, in declaration order.</param>
/// <param name="uniqueKeys">Its primary key and unique constraints, and unique indexes without a filter declared with it.</param>
public sealed class Table(ObjectName name, IReadOnlyList<Column> columns, IReadOnlyList<string> primaryKey, SourceSpan span, IReadOnlyList<CheckConstraint>? checks = null, IReadOnlyList<UniqueKey>? uniqueKeys = null)
{
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

    /// <summary>Where the table's definition is; its columns and constraints are positions in its file.</summary>
    public SourceSpan Span { get; } = span ?? throw new ArgumentNullException(nameof(span));

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
