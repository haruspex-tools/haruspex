# The null rule `unmatched-row`

A null rule, `IrNullRule.UnmatchedRow` in the IR's model. A value may be null, or a result missing, because it comes from the optional side of an outer join, which has no row when nothing matches.

## What it means

It comes from the optional side of an outer join, which has no row when nothing matches: a left join's right side, or either side of a full join.

## What to do

To change it, if the data rules it out, declare it in the config file with `"nullable": false` and why.

## What it rests on

Observed against SQL Server in `A_condition_in_the_on_of_an_outer_join_never_drops_a_row_of_the_kept_side` (`RefinementObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
