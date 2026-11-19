# The null rule `subtotal-row`

A null rule, `IrNullRule.SubtotalRow` in the IR's model. A value may be null, or a result missing, because `ROLLUP`, `CUBE` or `GROUPING SETS` add subtotal rows, which hold null in it.

## What it means

It is a grouped column, and the query adds subtotal rows, which total over it and hold NULL in it (super-aggregate rows: `ROLLUP`, `CUBE`, `GROUPING SETS`), whatever the filters proved of it.


## What it rests on

Observed against SQL Server in `Rollup_cube_and_grouping_sets_add_rows_whose_grouped_columns_are_null` (`RefinementObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
