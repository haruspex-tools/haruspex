# The unknown reason `unresolved-table`

An unknown reason, `IrUnknownReason.UnresolvedTable` in the IR's model. A value or a result is unknown because a table it reads is not defined in the project.

## What it means

A table that is not defined in the project.

## What to do

To change it, declare the results in the config file's `"results"`.

## What it rests on

There is no engine rule to observe here: it says what haruspex does. `A_view_outside_the_project_stays_unknown` (`ApplyViewTests`) shows haruspex giving it.

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
