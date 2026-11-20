using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>The procedure created the table, so its columns and what each may hold are known.</summary>
/// <param name="Name">The table's name, <c>#t</c>.</param>
/// <param name="Columns">Its columns, in order.</param>
internal sealed record KnownTable(LocalName Name, EquatableArray<TempColumn> Columns) : TempTable
{
    /// <summary>
    /// Its primary key, unique constraints and unfiltered unique indexes as they stand here: declared by its <c>CREATE TABLE</c>, or
    /// added since by <c>ALTER TABLE … ADD</c> or <c>CREATE UNIQUE INDEX</c>; one an <c>ALTER INDEX … DISABLE</c> disabled keeps
    /// where (<see cref="UniqueKey.DisabledAt"/>) until a <c>REBUILD</c> enables it again. A key's name is null when SQL Server
    /// names it, or when the paths that meet named it differently, so any name the body doesn't know may be its.
    /// </summary>
    /// <remarks>
    /// SQL Server enforces each as it does a table's (observed: A_temporary_table_enforces_the_keys_its_create_table_declares,
    /// A_key_added_to_a_temporary_table_holds_from_that_statement_on). A statement that adds one fails when rows already break it, or
    /// a primary key's column allows NULL, and the failure ends the call or reaches <c>CATCH</c> from the state before the
    /// statement (A_key_that_fails_to_be_added_fails_the_call_or_reaches_catch_without_it), so on every path that goes on, it holds
    /// (docs/decisions/0083-a-body-s-own-table-has-the-keys-sql-server-enforces-at-each-statement.md).
    /// </remarks>
    public EquatableArray<UniqueKey> Keys { get; init; } = [];

    /// <summary>The table with <paramref name="added"/> among its keys, unless it names a column the table lacks, which SQL Server refuses.</summary>
    public KnownTable WithKey(UniqueKey added) =>
        added.Columns.All(c => IndexOf(c) >= 0) ? this with { Keys = [.. Keys, added] } : this;

    /// <summary>
    /// The table after a statement drops the constraint or index named <paramref name="name"/>: the key of that name goes, and so
    /// does every key whose name isn't known, which it may be (its name SQL Server's, or one of two).
    /// </summary>
    public KnownTable Dropped(string name) => this with { Keys = [.. Keys.Where(k => !MayBe(k, name))] };

    /// <summary>
    /// The table after <c>ALTER INDEX … DISABLE</c> of <paramref name="name"/>, or of every index when it is null (<c>ALL</c>):
    /// a disabled index enforces nothing (observed: A_temporary_table_s_key_holds_until_it_is_dropped_or_disabled).
    /// </summary>
    public KnownTable Disabled(string? name, SourceLocation at) =>
        this with { Keys = [.. Keys.Select(k => name is null || MayBe(k, name) ? k with { DisabledAt = k.DisabledAt ?? at } : k)] };

    /// <summary>
    /// The table after <c>ALTER INDEX … REBUILD</c> of <paramref name="name"/>, or of every index when it is null (<c>ALL</c>): it
    /// enables a disabled index again, failing when rows written meanwhile break it, and a key whose name isn't known stays
    /// disabled, since the index rebuilt may be another.
    /// </summary>
    public KnownTable Rebuilt(string? name) =>
        this with { Keys = [.. Keys.Select(k => name is null || (k.Name is { } own && own.Equals(name, StringComparison.OrdinalIgnoreCase)) ? k with { DisabledAt = null } : k)] };

    /// <summary>The table without the keys over <paramref name="column"/>, whose type or existence a statement changed.</summary>
    public KnownTable WithoutKeysOver(string column) =>
        this with { Keys = [.. Keys.Where(k => !k.Columns.Contains(column, StringComparer.OrdinalIgnoreCase))] };

    /// <summary>The position of the column named <paramref name="column"/>, ignoring case as SQL Server does; -1 when there is none.</summary>
    public int IndexOf(string column) => Columns.ToList().FindIndex(c => c.Name.Equals(column, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The keys that hold of the table on two paths that meet: those both have, over the same columns, each disabled if either
    /// path disabled it, and named only when both name it alike.
    /// </summary>
    public static EquatableArray<UniqueKey> JoinKeys(KnownTable a, KnownTable b) =>
    [
        .. a.Keys.SelectMany(k => b.Keys.Where(l => Same(k, l)).Take(1).Select(l => k with
        {
            Name = k.Name is { } x && l.Name is { } y && x.Equals(y, StringComparison.OrdinalIgnoreCase) ? k.Name : null,
            DisabledAt = k.DisabledAt ?? l.DisabledAt,
        })),
    ];

    private static bool Same(UniqueKey k, UniqueKey l) =>
        k.Columns.Count == l.Columns.Count && k.Columns.All(c => l.Columns.Contains(c, StringComparer.OrdinalIgnoreCase));

    private static bool MayBe(UniqueKey key, string name) => key.Name is not { } own || own.Equals(name, StringComparison.OrdinalIgnoreCase);
}
