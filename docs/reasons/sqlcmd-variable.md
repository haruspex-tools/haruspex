# The unknown reason `sqlcmd-variable`

An unknown reason, `IrUnknownReason.SqlcmdVariable` in the IR's model. A value or a result is unknown because it holds a SQLCMD variable, which a deployment replaces with its value.

## What it means

A literal or a name holding a SQLCMD variable, `$(Region)`, that a deployment replaces with the variable's value before the server sees the text. The value may have any length, the empty one too, and may even end the string, so nothing drawn from the written text holds: not its length, its value, nor its type.

## What to do

To change it, declare the results in the config file's `"results"`.

## What it rests on

Observed against SQL Server in `A_deployment_puts_the_value_in_place_of_a_variable_in_a_literal_whatever_its_length` (`SqlcmdVariableObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
