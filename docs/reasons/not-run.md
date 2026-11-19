# The null rule `not-run`

A null rule, `IrNullRule.NotRun` in the IR's model. A value may be null, or a result missing, because the statement that returns it may not run.

## What it means

It is a result whose statement may not run (it is in a branch, or after a `RETURN` that may be taken), so the result set may be missing altogether rather than empty. Only a result after which every result may be missing too has it, so the result sets a client receives are always the first ones listed.


## What it rests on

Observed against SQL Server in `A_result_set_whose_statement_does_not_run_is_not_handed_over` (`MssqlDriverObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
