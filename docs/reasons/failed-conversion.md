# The null rule `failed-conversion`

A null rule, `IrNullRule.FailedConversion` in the IR's model. A value may be null, or a result missing, because `TRY_CAST` and `TRY_CONVERT` give null for a value that doesn't convert.

## What it means

It is `TRY_CAST` or `TRY_CONVERT` of a value that may not convert, which gives NULL instead of an error.


## What it rests on

Observed against SQL Server in `Try_cast_and_try_convert_are_null_when_the_conversion_fails` (`ExpressionObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
