# The cardinality `many`

A cardinality, `IrCardinality.Many` in the IR's model. A result returns any number of rows, none included: nothing bounds them.

## What it means

Any number, none included: nothing is proved.


## What it rests on

Observed against SQL Server in `A_full_join_of_two_queries_of_at_most_one_row_can_return_two` (`SetJoinObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
