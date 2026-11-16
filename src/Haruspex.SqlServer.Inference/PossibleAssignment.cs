using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// What a <c>SELECT @a = …, @b = … FROM …</c> that may read no row assigns when it does read one: each variable's value then,
/// before it is joined with what the variable held. A guard that proves the SELECT read a row puts these values back
/// (<c>Walker.Narrowed</c>): <c>IF @@ROWCOUNT = 0 RETURN</c> straight after it, or a test that one of its witnesses is not
/// NULL. It holds until any of its variables is assigned again (<c>Walker.Assign</c> drops it), and where paths meet only if
/// every path holds it (<see cref="FlowState.Join(FlowState?, FlowState?, SourceLocation)"/>).
/// </summary>
/// <remarks>
/// Every row a SELECT reads assigns every variable it lists, so one row read is enough (observed:
/// Rowcount_right_after_a_select_that_assigns_counts_its_rows). A witness proves the read only because it was NULL on every
/// path before the SELECT and nothing but the SELECT has assigned it since: a value in it then came from the SELECT
/// (observed: A_variable_null_before_a_select_and_not_after_it_proves_the_select_assigned_them_all). Docs: decision 0052.
/// </remarks>
/// <param name="Values">Each variable the SELECT assigns, with the value it holds when the SELECT read a row.</param>
/// <param name="Witnesses">Those of its variables that held NULL on every path before it (<see cref="FlowState.Unassigned"/>).</param>
/// <param name="Counted">
/// Whether <c>@@ROWCOUNT</c> still counts the SELECT's rows: only for the statement straight after it, which an <c>IF</c>
/// testing it must be; any statement between resets it (observed: Any_statement_between_resets_rowcount_so_the_test_proves_nothing).
/// </param>
internal sealed record PossibleAssignment(EquatableMap<LocalName, Value> Values, EquatableArray<LocalName> Witnesses, bool Counted);
