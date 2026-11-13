using Haruspex.Collections;
using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>A table: its columns as declared, its keys and its CHECK constraints.</summary>
/// <remarks>
/// The model holds what the source declares and nothing it implies. Whether a column can be null, for example,
/// follows from its declaration, its key and its identity together; that interpretation belongs to whoever reads
/// the model (in haruspex, Haruspex.SqlServer.Semantics), not to the model.
/// </remarks>
/// <param name="Name">Its name.</param>
/// <param name="Span">Where its definition is; its columns and constraints are positions in its file.</param>
/// <param name="Columns">Its columns, in declaration order.</param>
/// <param name="PrimaryKey">
/// The names of its primary key's columns, in key order, whether it is declared on the table or on a column. Empty when there
/// is none.
/// </param>
/// <param name="Checks">Its CHECK constraints, whether declared on the table or on a column, in declaration order.</param>
/// <param name="UniqueKeys">
/// Its primary key, unique constraints and unique indexes, each a set of columns no two rows share (SQL Server counts NULLs as
/// equal there, so one NULL at most). Only what the table's definition declares; see <see cref="UniqueKey.DisabledAt"/>.
/// </param>
public sealed record Table(
    ObjectName Name,
    SourceSpan Span,
    EquatableArray<Column> Columns,
    EquatableArray<string> PrimaryKey,
    EquatableArray<CheckConstraint> Checks,
    EquatableArray<UniqueKey> UniqueKeys) : SchemaObject(Name, Span), ITable
{
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
}
