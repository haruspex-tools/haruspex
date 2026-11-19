# The null rule `nullable-column`

A null rule, `IrNullRule.NullableColumn` in the IR's model. A value may be null, or a result missing, because the column it reads is declared nullable.

## What it means

The column it reads is declared nullable (or not declared `NOT NULL`).

## What to do

To change it, if the data rules it out, declare it in the config file with `"nullable": false` and why.

## What it rests on

Observed against SQL Server in `A_column_that_declares_no_nullability_takes_its_alias_types_whatever_the_session_default` (`CheckConstraintObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
