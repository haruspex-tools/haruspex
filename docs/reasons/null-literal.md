# The null rule `null-literal`

A null rule, `IrNullRule.NullLiteral` in the IR's model. A value may be null, or a result missing, because it is, or may be, the `NULL` literal.

## What it means

A result of a `CASE`, `IIF` or `CHOOSE` it may give is the `NULL` literal, or it is (or reads) the `NULL` literal itself, as a bare `NULL` select item (an `int`) or `CAST(NULL AS date)`.


## What it rests on

Observed against SQL Server in `A_bare_null_is_an_int` (`FunctionObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
