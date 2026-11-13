using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// A variable the body declares (<c>DECLARE @v int</c>). Declarations are compiled, not run (observed:
/// A_variable_or_table_variable_declared_in_a_branch_not_taken_still_exists), so it exists wherever the body reads it.
/// </summary>
/// <param name="Name">Its name, with its <c>@</c>, as its declaration writes it.</param>
/// <param name="Type">Its declared type; null for a cursor variable, which has none.</param>
/// <param name="At">Where it is declared.</param>
public sealed record LocalVariable(LocalName Name, SqlType? Type, SourceLocation At) : VariableResolution
{
    /// <inheritdoc/>
    public override LocalName Name { get; } = Name;
}
