using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources;

/// <summary><c>ALTER INDEX name ON t DISABLE</c> or <c>ALTER INDEX ALL ON t DISABLE</c>.</summary>
/// <param name="Table">The table it names; in <c>dbo</c> when <paramref name="Qualified"/> is false.</param>
/// <param name="Qualified">Whether the table's name has its schema.</param>
/// <param name="Index">The index named, or null for ALL.</param>
/// <param name="At">Where it is.</param>
internal sealed record IndexDisable(ObjectName Table, bool Qualified, string? Index, SourceLocation At)
{
    /// <summary>Whether it may be about this table, by the rule <see cref="Enforcement.May"/> gives.</summary>
    public bool May(Table table) => Qualified
        ? Table == table.Name
        : Table.Name.Equals(table.Name.Name, StringComparison.OrdinalIgnoreCase);
}
