# The cardinality `atMostOne`

A cardinality, `IrCardinality.AtMostOne` in the IR's model. A result returns at most one row: `TOP (1)`, `FETCH NEXT 1`, or a filter that fixes a key.

## What it means

None or one: `TOP (1)`, `FETCH NEXT 1`, or a filter that fixes every column of a key.


## What it rests on

Observed against SQL Server in `Top_1_applied_gives_at_most_one_row_for_every_outer_row` (`ApplyViewObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
