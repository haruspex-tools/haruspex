# The unknown reason `unsupported-source`

An unknown reason, `IrUnknownReason.UnsupportedSource` in the IR's model. A value or a result is unknown because a table source or join is not modelled yet.

## What it means

A table source or join that is not modelled yet.

## What to do

To change it, declare the results in the config file's `"results"`.

## What it rests on

There is no engine rule to observe here: it says what haruspex does. `A_source_not_modelled_makes_its_columns_unknown` (`ConstructTests`) shows haruspex giving it.

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
