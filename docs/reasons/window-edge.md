# The null rule `window-edge`

A null rule, `IrNullRule.WindowEdge` in the IR's model. A value may be null, or a result missing, because `LAG` or `LEAD` without a default is null past the partition's edge.

## What it means

It is `LAG` or `LEAD` without a default, NULL where the row it reads is past the partition's edge.

## What to do

To change it, give `LAG` or `LEAD` a default.

## What it rests on

Observed against SQL Server in `Lag_and_lead_are_null_past_the_partitions_edge_unless_a_default_is_given_which_is_converted_to_the_columns_type` (`FunctionObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
