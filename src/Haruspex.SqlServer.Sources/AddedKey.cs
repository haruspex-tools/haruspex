using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// A primary key, unique constraint or unique index added to a table after its definition, by <c>ALTER TABLE … ADD
/// CONSTRAINT</c> or <c>CREATE UNIQUE INDEX</c> without a filter, in a statement of a batch, naming the table with its schema
/// and no database or server (one with either is another database's table).
/// AdventureWorks adds every key this way.
/// </summary>
/// <remarks>
/// Such a key is enforced as one the table declares (observed: A_key_added_by_alter_table_is_enforced_as_one_the_table_declares,
/// A_unique_index_created_after_its_table_is_enforced_as_one_the_table_declares): a primary key can't be added over a nullable
/// column, <c>WITH NOCHECK</c> doesn't apply to a key and an index has no such option, so rows already sharing a key, NULL
/// included, refuse it, and <c>NOCHECK CONSTRAINT</c> can't disable it; only <c>ALTER INDEX … DISABLE</c>, a drop or an index
/// that replaces it (<c>DROP_EXISTING</c>) can, which <see cref="ConstraintTrust"/> follows for it as for any key. A filtered
/// index is unique only among the rows its filter keeps, so it is no key. A table name without its schema may mean another table (SQL Server looks in the caller's default schema
/// first), and one inside a body or under <c>IF</c> may never run, so neither is read, which only claims less.
/// </remarks>
/// <param name="Table">The table it is added to.</param>
/// <param name="Key">The key, where the <c>ALTER TABLE</c> declares it.</param>
internal sealed record AddedKey(ObjectName Table, UniqueKey Key)
{
    /// <summary>
    /// The table with the keys added to it. One that names a column the table lacks is passed over, since SQL Server refuses it;
    /// a primary key is the table's primary key, unless it declares one already, which SQL Server refuses too.
    /// </summary>
    public static Table Into(Table table, IEnumerable<AddedKey> added)
    {
        var keys = added.Where(a => a.Table == table.Name && a.Key.Columns.All(c => table.Column(c) is not null)).Select(a => a.Key).ToList();
        if (keys.Count == 0)
            return table;
        var primary = table.PrimaryKey.Count == 0 && keys.FirstOrDefault(k => k.IsPrimaryKey) is { } key ? key.Columns : table.PrimaryKey;
        return table with { PrimaryKey = primary, UniqueKeys = [.. table.UniqueKeys, .. keys] };
    }
}
