using Haruspex.Collections;
using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// A table a body declares for itself, which is not an object of the schema: a temporary table (<c>CREATE TABLE #t</c>), a
/// table variable (<c>DECLARE @t TABLE</c>, or <c>DECLARE @t sales.line_list</c> of a <see cref="TableType"/>), a table-valued
/// parameter, or the table a multi-statement or CLR table-valued function returns. It is read
/// as the schema's tables are, so the schema's rules apply to it (<see cref="ITable"/>), but it belongs to no schema: its name
/// is the body's, with its sigil, and a fact its CHECK constraints prove is named by it (<c>#totals.ck_amount</c>).
/// </summary>
/// <param name="Name">Its name as the body writes it: <c>#t</c>, <c>@t</c>; a CLR function's table, which has no variable, the function's.</param>
/// <param name="Span">Where its declaration is.</param>
/// <param name="Columns">Its columns, in declaration order.</param>
/// <param name="PrimaryKey">The names of its primary key's columns, in key order; empty when it has none.</param>
/// <param name="Checks">Its CHECK constraints, in declaration order.</param>
/// <param name="UniqueKeys">Its primary key, unique constraints and unique indexes.</param>
public sealed record DeclaredTable(
    string Name,
    SourceSpan Span,
    EquatableArray<Column> Columns,
    EquatableArray<string> PrimaryKey,
    EquatableArray<CheckConstraint> Checks,
    EquatableArray<UniqueKey> UniqueKeys) : ITable
{
    /// <summary>
    /// The table type it was declared with: a table-valued parameter's (<c>@lines sales.line_list READONLY</c>) or a table
    /// variable's (<c>DECLARE @t sales.line_list</c>); null for one declared with its own columns.
    /// </summary>
    public TableType? Type { get; init; }

    /// <summary>
    /// Whether it is a table variable: one a body declares, a table-valued parameter or the table a multi-statement function
    /// returns, each named with <c>@</c>; not a temporary table, nor a CLR function's table.
    /// </summary>
    public bool IsVariable => Name.StartsWith('@');

    /// <summary>Never: a body's tables can't be graph tables.</summary>
    public bool IsGraph => false;

    /// <summary>
    /// The column with this name, ignoring case, or null. Ignoring case is an assumption, which a table whose columns differ only in
    /// case would break: reading refuses one (HAR1022, decision 0119), and the binder reads either column of a pair it is given as
    /// unknown (decision 0107).
    /// </summary>
    /// <param name="name">The column's name.</param>
    public Column? Column(string name) =>
        Columns.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The table's name.</summary>
    public override string ToString() => Name;
}
