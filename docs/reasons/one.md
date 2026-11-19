# The cardinality `one`

A cardinality, `IrCardinality.One` in the IR's model. A result returns exactly one row: an aggregate without `GROUP BY`, or a query without `FROM`.

## What it means

Exactly one: an aggregate query without `GROUP BY` or `HAVING`, or a query without `FROM` or a filter.


## What it rests on

Observed against SQL Server in `An_aggregate_without_group_by_returns_exactly_one_row_even_over_no_rows` (`RefinementObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
