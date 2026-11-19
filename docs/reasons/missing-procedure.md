# The unknown reason `missing-procedure`

An unknown reason, `IrUnknownReason.MissingProcedure` in the IR's model. A value or a result is unknown because it calls a procedure the project doesn't define.

## What it means

A procedure that is not in the project: in another database, on a linked server, or nowhere.

## What to do

To change it, declare the results in the config file's `"results"`.

## What it rests on

There is no engine rule to observe here: it says what haruspex does. `A_procedure_not_in_the_project_is_unknown` (`ExecTests`) shows haruspex giving it.

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
