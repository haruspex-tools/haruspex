# The unknown reason `unresolved-column`

An unknown reason, `IrUnknownReason.UnresolvedColumn` in the IR's model. A value or a result is unknown because a column can't be found, or is ambiguous, in the tables in scope.

## What it means

A column that cannot be found, or is ambiguous, in the tables in scope.

## What to do

To change it, declare the results in the config file's `"results"`.

## What it rests on

There is no engine rule to observe here: it says what haruspex does. `An_ambiguous_unqualified_column_is_unknown` (`ConstructTests`) shows haruspex giving it.

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
