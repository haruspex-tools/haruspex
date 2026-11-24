using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// <c>ALTER TABLE … ALTER COLUMN … ADD MASKED WITH (FUNCTION = '…')</c>, which masks a column after its table is defined, or
/// <c>… DROP MASKED</c>, which removes its mask: wherever it is, in the order read.
/// </summary>
/// <remarks>
/// <para>
/// Neither changes the column's type or whether it may be null, so neither is a change the model can't apply (observed:
/// A_mask_added_or_dropped_later_changes_the_column_s_mask_alone). What a mask does to the claims is the model's to record and
/// inference's to apply (<see cref="Column.Mask"/>).
/// </para>
/// <para>
/// <b>Which is followed.</b> A mask claims less, so an <c>ADD MASKED</c> that may run masks the column, wherever it is: under
/// <c>IF</c>, in a body, in another script or in a deployment script. A <c>DROP MASKED</c> claims more, so it is followed only when
/// it certainly runs after every mask the column may have: a batch's own statement in the script that defines the table, after
/// the definition, naming the table with its schema, with every <c>ADD MASKED</c> of the column a batch's own statement before it
/// in the same script. A folder doesn't say the order its scripts run in, and whether a body runs isn't knowable, so any other
/// drop leaves the mask, which only claims less. This is how <see cref="ConstraintTrust"/> follows a check validated again.
/// </para>
/// </remarks>
/// <param name="Table">The table it names; in <c>dbo</c> when <paramref name="Qualified"/> is false.</param>
/// <param name="Qualified">Whether the table's name has its schema.</param>
/// <param name="Column">The column it names.</param>
/// <param name="Mask">The function an <c>ADD MASKED</c> masks it with, as written; null for <c>DROP MASKED</c>.</param>
/// <param name="At">Where it is.</param>
/// <param name="TopLevel">Whether it is a statement of a batch, rather than inside IF, a block, or a body.</param>
internal sealed record MaskChange(ObjectName Table, bool Qualified, string Column, string? Mask, SourceLocation At, bool TopLevel)
{
    /// <summary>
    /// The table with each column's mask as the changes that may name it leave it (see the remarks). A change that names a column
    /// the table lacks is passed over, since SQL Server refuses it.
    /// </summary>
    public static Table Applied(Table table, IReadOnlyList<MaskChange> changes)
    {
        var relevant = changes.Where(c => c.May(table)).ToList();
        if (relevant.Count == 0)
            return table;

        var columns = table.Columns.Select(column =>
        {
            var named = relevant.Where(c => c.Column.Equals(column.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            var added = named.Where(c => c.Mask is not null).ToList();
            var mask = added.Count > 0 ? added[^1].Mask : column.Mask;
            if (mask is not null && named.Any(drop => drop.Mask is null && drop.CertainlyLast(table, added)))
                mask = null;
            return mask == column.Mask ? column : column with { Mask = mask };
        });
        return table with { Columns = [.. columns] };
    }

    /// <summary>
    /// Whether it may be about this table: the one it names, or, when its name has no schema, any table of that name, since SQL
    /// Server looks in the caller's default schema first and the source does not say whose.
    /// </summary>
    private bool May(Table table) => Qualified
        ? Table == table.Name
        : Table.Name.Equals(table.Name.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this drop certainly runs after the table's definition and after every <c>ADD MASKED</c> of the column (see the
    /// remarks).
    /// </summary>
    private bool CertainlyLast(Table table, List<MaskChange> added) =>
        TopLevel && Qualified && At.File == table.Span.File && After(At, table.Span.Start)
        && added.All(a => a.TopLevel && a.At.File == At.File && After(At, a.At));

    private static bool After(SourceLocation later, SourceLocation earlier) =>
        later.Line > earlier.Line || (later.Line == earlier.Line && later.Column > earlier.Column);
}
