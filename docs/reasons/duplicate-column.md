# The unknown reason `duplicate-column`

An unknown reason, `IrUnknownReason.DuplicateColumn` in the IR's model. A value or a result is unknown because two result columns claim the same key.

## What it means

Two result columns that claim the same key.

## What to do

To change it, give each column a name of its own.

## What it rests on

There is no engine rule to observe here: it says what haruspex does. `A_repeated_rowset_column_name_makes_the_result_unknown` (`ConstructTests`) shows haruspex giving it.

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
