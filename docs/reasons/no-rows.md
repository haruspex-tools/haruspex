# The null rule `no-rows`

A null rule, `IrNullRule.NoRows` in the IR's model. A value may be null, or a result missing, because the query returns nothing when no rows match.

## What it means

It is a query result, which returns nothing when no rows match.


## What it rests on

Observed against SQL Server in `A_top_level_for_json_query_with_no_rows_returns_a_result_set_with_no_row` (`ForJsonObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
