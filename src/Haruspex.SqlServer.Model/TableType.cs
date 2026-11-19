using Haruspex.Collections;
using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// A user-defined table type (<c>CREATE TYPE sales.line_list AS TABLE (…)</c>): the definition a table-valued parameter and a
/// table variable declared with it (<c>DECLARE @t sales.line_list</c>) take their columns, keys and CHECK constraints from. It is
/// read as a table is, so the schema's rules read it through <see cref="ITable"/>.
/// </summary>
/// <remarks>
/// Its constraints are part of the type, and SQL Server enforces them on every row a caller sends and every row a body writes,
/// so they hold of what a body reads; none can be disabled, since a type can't be altered (there is no <c>ALTER TYPE</c> for a
/// table type). A column's default is never applied to a row a caller sends (observed:
/// <c>A_default_never_applies_to_a_row_a_table_valued_parameter_is_sent</c>), so the model keeps that a column has one
/// (<see cref="Column.HasDefault"/>) but nothing reads it as a value.
/// </remarks>
/// <param name="Name">Its name.</param>
/// <param name="Span">Where its definition is; its columns and constraints are positions in its file.</param>
/// <param name="Columns">Its columns, in declaration order: the order a caller sends them in, since they are matched by place.</param>
/// <param name="PrimaryKey">The names of its primary key's columns, in key order; empty when it has none.</param>
/// <param name="Checks">Its CHECK constraints, whether declared on the type or on a column, in declaration order.</param>
/// <param name="UniqueKeys">Its primary key, unique constraints and unique indexes.</param>
public sealed record TableType(
    ObjectName Name,
    SourceSpan Span,
    EquatableArray<Column> Columns,
    EquatableArray<string> PrimaryKey,
    EquatableArray<CheckConstraint> Checks,
    EquatableArray<UniqueKey> UniqueKeys) : UserType(Name, Span), ITable
{
    /// <summary>Never: a table type can't be a graph table.</summary>
    public bool IsGraph => false;

    /// <summary>The column with this name, ignoring case as SQL Server does, or null.</summary>
    /// <param name="name">The column's name.</param>
    public Column? Column(string name) =>
        Columns.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The table a parameter or variable of this type is, under the name the body gives it (<c>@lines</c>), so that it is read as
    /// any other table a body declares for itself.
    /// </summary>
    /// <param name="variable">The parameter's or variable's name, with its <c>@</c>.</param>
    public DeclaredTable As(string variable) =>
        new(variable, Span, Columns, PrimaryKey, Checks, UniqueKeys) { Type = this };
}
