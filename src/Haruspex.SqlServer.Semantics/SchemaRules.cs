using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// What the schema implies beyond what it declares. The model holds declarations; the rules that turn them into
/// facts about values live here, so that lint, diff and inference all read a schema the same way.
/// </summary>
public static class SchemaRules
{
    /// <summary>
    /// Whether SQL Server guarantees the column is never null: it is declared NOT NULL, it is in the primary key
    /// (which implies NOT NULL), or it is an IDENTITY column (which cannot be nullable).
    /// </summary>
    /// <remarks>
    /// A column declared with neither NULL nor NOT NULL is nullable under the default ANSI settings; with
    /// ANSI_NULL_DFLT_OFF it would not be, so nullable is the claim that holds either way. A computed column is
    /// read by the same rules today; its expression is not yet used to prove it non-null.
    /// </remarks>
    public static bool IsNeverNull(Table table, Column column)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(column);
        return column.Nullability == DeclaredNullability.NotNull
            || table.IsInPrimaryKey(column.Name)
            || column.IsIdentity;
    }
}
