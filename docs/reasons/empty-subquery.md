# The null rule `empty-subquery`

A null rule, `IrNullRule.EmptySubquery` in the IR's model. A value may be null, or a result missing, because it is a subquery, which is null when it returns no rows.

## What it means

It is a subquery, which is null when it returns no rows.

## What to do

To change it, if the data rules it out, declare it in the config file with `"nullable": false` and why.

## What it rests on

Observed against SQL Server in `A_for_json_subquery_with_no_rows_is_null` (`ForJsonObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
