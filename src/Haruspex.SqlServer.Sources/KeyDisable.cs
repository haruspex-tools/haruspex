using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// A statement that may stop a table's key being enforced: <c>ALTER INDEX name ON t DISABLE</c> or <c>ALTER INDEX ALL ON t
/// DISABLE</c>, <c>ALTER TABLE t DROP CONSTRAINT name</c>, or <c>DROP INDEX name ON t</c> (or <c>t.name</c>). A disabled key
/// enforces nothing, and a dropped one is gone, so rows written meanwhile may share a key (observed:
/// A_disabled_unique_index_allows_duplicates, A_dropped_key_holds_no_longer_and_its_primary_keys_columns_stay_not_null).
/// </summary>
/// <param name="Table">The table it names; in <c>dbo</c> when <paramref name="Qualified"/> is false.</param>
/// <param name="Qualified">Whether the table's name has its schema.</param>
/// <param name="Key">The index or constraint named, or null for ALL.</param>
/// <param name="At">Where it is.</param>
internal sealed record KeyDisable(ObjectName Table, bool Qualified, string? Key, SourceLocation At)
{
    /// <summary>Whether it may be about this table, by the rule <see cref="Enforcement.May"/> gives.</summary>
    public bool May(Table table) => Qualified
        ? Table == table.Name
        : Table.Name.Equals(table.Name.Name, StringComparison.OrdinalIgnoreCase);
}
