# The unknown reason `external-code`

An unknown reason, `IrUnknownReason.ExternalCode` in the IR's model. A value or a result is unknown because it is made by code outside the project, such as a CLR procedure.

## What it means

Results produced by code outside the project, such as a CLR procedure.

## What to do

To change it, declare the results in the config file's `"results"`.

## What it rests on

There is no engine rule to observe here: it says what haruspex does. `A_clr_procedure_has_unknown_results` (`ConstructTests`) shows haruspex giving it.

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
