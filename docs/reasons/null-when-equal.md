# The null rule `null-when-equal`

A null rule, `IrNullRule.NullWhenEqual` in the IR's model. A value may be null, or a result missing, because `NULLIF` is null when its two arguments are equal.

## What it means

It is made NULL when it equals a given value (`NULLIF`, NULL when its two arguments compare equal).


## What it rests on

Observed against SQL Server in `Case_without_else_choose_out_of_range_and_nullif_of_equal_values_are_null` (`CaseObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
