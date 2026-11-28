# The null rule `session-state`

A null rule, `IrNullRule.SessionState` in the IR's model. A value may be null because it reads the session's state, which holds no value in some states.

## What it means

It reads the state of the session or of the call (0.38), which in some states holds no value, so the server gives NULL:
`SCOPE_IDENTITY()` and `@@IDENTITY` before an insert gives an identity, and after an insert into a table without one;
`ERROR_MESSAGE()` and the other error functions outside a `CATCH`, and `ERROR_PROCEDURE()` inside one for an error raised
outside a module. The reason's detail says which states. Where the value is read never removes it: `ERROR_MESSAGE()` inside a
`CATCH`, and `SCOPE_IDENTITY()` straight after an `INSERT`, have it too
([decision 0146](../decisions/0146-a-session-value-is-nullable-unless-it-is-never-null-in-any-state.md)). `ISNULL` or
`COALESCE` with a value never null removes it, as for any reason.

## What it rests on

Observed against SQL Server in `Scope_identity_and_the_last_identity_are_numeric_38_0_and_null_after_an_insert_into_a_table_without_one` (`FunctionObservationTests`),
and for the error functions in `Error_functions_are_null_outside_a_catch_and_error_procedure_inside_one_for_an_error_outside_a_module`
(`FunctionObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
