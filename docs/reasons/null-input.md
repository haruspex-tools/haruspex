# The null rule `null-input`

A null rule, `IrNullRule.NullInput` in the IR's model. A value may be null, or a result missing, because it calls a function that returns null on null input with an argument that may be null.

## What it means

It is a call of a function declared `WITH RETURNS NULL ON NULL INPUT` with an argument that may be null: such a call is NULL without running the body.


## What it rests on

Observed against SQL Server in `A_function_returns_null_when_for_json_finds_no_rows_or_an_argument_is_null` (`JsonFunctionObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
