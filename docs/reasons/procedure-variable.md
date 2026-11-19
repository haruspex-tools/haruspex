# The unknown reason `procedure-variable`

An unknown reason, `IrUnknownReason.ProcedureVariable` in the IR's model. A value or a result is unknown because the procedure it calls is named in a variable.

## What it means

`EXEC @name`: the procedure is chosen when the call runs.

## What to do

To change it, declare the results in the config file's `"results"`.

## What it rests on

There is no engine rule to observe here: it says what haruspex does. `A_procedure_named_in_a_variable_is_unknown` (`ExecTests`) shows haruspex giving it.

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
