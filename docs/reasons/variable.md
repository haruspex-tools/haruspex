# The null rule `variable`

A null rule, `IrNullRule.Variable` in the IR's model. A value may be null, or a result missing, because it is a variable that may not have been given a value that is never null.

## What it means

It is a procedure's variable, which holds null until a value that is never null is assigned on every path.

## What to do

To change it, if the data rules it out, declare it in the config file with `"nullable": false` and why.

## What it rests on

Observed against SQL Server in `A_variable_null_before_a_select_and_not_after_it_proves_the_select_assigned_them_all` (`StatementFlowObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
