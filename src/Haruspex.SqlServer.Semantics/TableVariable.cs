using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// A table variable, <c>@t</c>, as its <c>DECLARE</c> declares it, or a multi-statement function's return table. Declarations
/// are compiled, not run (observed: A_variable_or_table_variable_declared_in_a_branch_not_taken_still_exists), so its columns
/// are the declaration's wherever it is read.
/// </summary>
/// <param name="Declaration">Its declaration, a table named by the variable with no schema.</param>
public sealed record TableVariable(Table Declaration)
    : RelationSymbol(Declaration?.Name.Name ?? throw new ArgumentNullException(nameof(Declaration)), [.. Declaration.Columns.Select(c => c.Name)]);
