# The null rule `unmatched-lateral-row`

A null rule, `IrNullRule.UnmatchedLateralRow` in the IR's model. A value may be null, or a result missing, because it comes from an `OUTER APPLY` whose query may return no row.

## What it means

It comes from the right side of an outer lateral join (SQL Server's `OUTER APPLY`), whose query returns no row for some row of the left. A lateral query that always returns a row (an aggregate without `GROUP BY`, a query without `FROM`, a `VALUES` list) never gives it. Apart from `IrNullRule.UnmatchedRow` because what removes it differs: a query that always returns a row, rather than a condition that the row matched.

## What to do

To change it, if the data rules it out, declare it in the config file with `"nullable": false` and why.

## What it rests on

Observed against SQL Server in `Cross_apply_keeps_only_outer_rows_with_a_right_row_and_outer_apply_fills_the_others_with_null` (`ApplyViewObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
