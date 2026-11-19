# The null rule `parameter`

A null rule, `IrNullRule.Parameter` in the IR's model. A value may be null, or a result missing, because it is a parameter, which a caller may pass as null.

## What it means

It is a function's parameter, which a caller may pass as NULL.


## What it rests on

Observed against SQL Server in `A_null_test_that_returns_or_throws_lets_only_a_value_past_it` (`StatementFlowObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
