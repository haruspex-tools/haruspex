using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// A pass over what the scripts declare: which of a table's CHECK constraints and unique keys a later statement may have
/// disabled, so that the model doesn't claim what they guarantee.
/// </summary>
/// <remarks>
/// A constraint guarantees nothing unless SQL Server trusts it: while disabled it checks no row, and rows written then stay
/// after a plain re-enable. So a NOCHECK that may be about the check disables it, wherever it is, since whether a statement in
/// a body or under IF runs is not knowable. Only <c>WITH CHECK CHECK CONSTRAINT</c> validates every row, and it undoes a
/// disable only when both are statements of batches in the same script, the re-enable after it, naming the table with its
/// schema and the check by name (or ALL): then it certainly runs after it on that check. Across scripts the order isn't known.
/// A constraint added later by ALTER TABLE, with or without WITH NOCHECK, is not read at all
/// (<see cref="ScriptReader"/>, "only tightens").
/// </remarks>
internal static class ConstraintTrust
{
    /// <summary>The table with each check a NOCHECK may have left untrusted, and each key a disabled index may leave unenforced, marked so.</summary>
    public static Table Trusted(Table table, IReadOnlyList<Enforcement> changes, IReadOnlyList<IndexDisable> disables)
    {
        var keys = Keys(table, disables);
        var relevant = changes.Where(c => c.May(table)).ToList();
        if (relevant.Count == 0)
            return keys == table.UniqueKeys ? table : table with { UniqueKeys = keys };

        var checks = table.Checks.Select(check =>
        {
            var disabled = relevant.FirstOrDefault(d => !d.Validates && d.MayName(check, table) && !ValidatedLater(d, check, relevant));
            return disabled is null ? check : check with { DisabledAt = disabled.At };
        }).ToList();
        return table with { Checks = checks, UniqueKeys = keys };
    }

    /// <summary>Whether a <c>WITH CHECK CHECK CONSTRAINT</c> certainly runs after <paramref name="disable"/> on the check (see the remarks).</summary>
    private static bool ValidatedLater(Enforcement disable, CheckConstraint check, List<Enforcement> relevant) =>
        disable.TopLevel && relevant.Any(v => v.Validates && v.TopLevel && v.Qualified && v.Names(check)
            && v.At.File == disable.At.File && v.Order > disable.Order);

    /// <summary>
    /// The table's keys, each one a disable may name marked as possibly disabled: ALL, its name (a key's index has the
    /// constraint's name), or, for a key declared without a name, any name the table doesn't declare, since SQL Server
    /// generates one. Re-enabling (a rebuild) is not followed, which only claims less.
    /// </summary>
    private static IReadOnlyList<UniqueKey> Keys(Table table, IReadOnlyList<IndexDisable> disables)
    {
        var relevant = disables.Where(d => d.May(table)).ToList();
        if (relevant.Count == 0)
            return table.UniqueKeys;

        bool Names(IndexDisable d, UniqueKey key) =>
            d.Index is null
            || (key.Name is not null && key.Name.Equals(d.Index, StringComparison.OrdinalIgnoreCase))
            || (key.Name is null && !table.UniqueKeys.Any(k => d.Index.Equals(k.Name, StringComparison.OrdinalIgnoreCase)));

        return [.. table.UniqueKeys.Select(key => relevant.FirstOrDefault(d => Names(d, key)) is { } disable ? key with { DisabledAt = disable.At } : key)];
    }
}
