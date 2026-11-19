# The unknown reason `redefined`

An unknown reason, `IrUnknownReason.Redefined` in the IR's model. A value or a result is unknown because the object is defined more than once, so which definition applies depends on order.

## What it means

An object defined more than once by statements whose order decides which applies, such as a CREATE and an ALTER PROCEDURE, or a definition followed by a DROP.

## What to do

To change it, declare the results in the config file's `"results"`.

## What it rests on

There is no engine rule to observe here: it says what haruspex does. `A_procedure_changed_after_its_create_has_unknown_results` (`ConstructTests`) shows haruspex giving it.

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
