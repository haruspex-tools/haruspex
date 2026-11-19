# The null rule `empty-aggregate`

A null rule, `IrNullRule.EmptyAggregate` in the IR's model. A value may be null, or a result missing, because it is an aggregate without `GROUP BY`, which is null over no rows.

## What it means

It is an aggregate (`MIN`, `MAX`, `json_arrayagg`, `json_objectagg`) without GROUP BY, which is null over no rows.

## What to do

To change it, if the data rules it out, declare it in the config file with `"nullable": false` and why.

## What it rests on

Observed against SQL Server in `An_aggregate_over_no_rows_is_null_not_empty` (`JsonConstructorObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
