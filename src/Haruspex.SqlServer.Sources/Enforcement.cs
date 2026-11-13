using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources;

/// <summary>ALTER TABLE … NOCHECK CONSTRAINT, or … WITH CHECK CHECK CONSTRAINT.</summary>
/// <param name="Table">The table it names; in <c>dbo</c> when <paramref name="Qualified"/> is false.</param>
/// <param name="Qualified">Whether the table's name has its schema.</param>
/// <param name="Validates">True for WITH CHECK CHECK CONSTRAINT; false for NOCHECK.</param>
/// <param name="Constraints">The constraints named, or null for ALL.</param>
/// <param name="At">Where it is.</param>
/// <param name="Order">Its position among all such statements, in reading order.</param>
/// <param name="TopLevel">Whether it is a statement of a batch, rather than inside IF, a block, or a body.</param>
internal sealed record Enforcement(ObjectName Table, bool Qualified, bool Validates, EquatableArray<string>? Constraints, SourceLocation At, int Order, bool TopLevel)
{
    /// <summary>
    /// Whether it may be about this table: the one it names, or, when its name has no schema, any table of that
    /// name, since SQL Server looks in the caller's default schema first and the source does not say whose.
    /// </summary>
    public bool May(Table table) => Qualified
        ? Table == table.Name
        : Table.Name.Equals(table.Name.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether it may name <paramref name="check"/>: ALL, its name, or, for a check declared without a name, a
    /// name the table doesn't declare. SQL Server generates a name for an unnamed constraint (CK__t__c__3A81B327),
    /// which a script copied from a live database can use.
    /// </summary>
    public bool MayName(CheckConstraint check, Table table) =>
        Constraints is null
        || (check.Name is not null && Constraints.Contains(check.Name, StringComparer.OrdinalIgnoreCase))
        || (check.Name is null && Constraints.Any(n => !table.Checks.Any(c => n.Equals(c.Name, StringComparison.OrdinalIgnoreCase))));

    /// <summary>Whether it certainly names <paramref name="check"/>, which a re-enable must, to count.</summary>
    public bool Names(CheckConstraint check) =>
        Constraints is null || (check.Name is not null && Constraints.Contains(check.Name, StringComparer.OrdinalIgnoreCase));
}
