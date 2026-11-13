using Haruspex.Collections;
using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// What a table's declaration gives, whoever declares it: its columns, keys and CHECK constraints. A table of the schema
/// (<see cref="Table"/>) and a table a body declares for itself (<see cref="DeclaredTable"/>) both have them, so the schema's
/// rules (which columns are never null, what a CHECK allows, which keys hold) read either.
/// </summary>
public interface ITable
{
    /// <summary>Where its declaration is; its columns and constraints are positions in that file.</summary>
    SourceSpan Span { get; }

    /// <summary>Its columns, in declaration order.</summary>
    EquatableArray<Column> Columns { get; }

    /// <summary>The names of its primary key's columns, in key order; empty when it has none.</summary>
    EquatableArray<string> PrimaryKey { get; }

    /// <summary>Its CHECK constraints, in declaration order.</summary>
    EquatableArray<CheckConstraint> Checks { get; }

    /// <summary>Its primary key, unique constraints and unique indexes (see <see cref="UniqueKey.DisabledAt"/>).</summary>
    EquatableArray<UniqueKey> UniqueKeys { get; }

    /// <summary>Whether it is a graph table, whose <c>SELECT *</c> includes pseudo-columns the declaration does not list.</summary>
    bool IsGraph { get; }

    /// <summary>The column with this name, ignoring case as SQL Server does, or null.</summary>
    /// <param name="name">The column's name.</param>
    Column? Column(string name);

    /// <summary>Whether the column is part of the primary key, ignoring case.</summary>
    /// <param name="column">The column's name.</param>
    bool IsInPrimaryKey(string column) => PrimaryKey.Contains(column, StringComparer.OrdinalIgnoreCase);
}
