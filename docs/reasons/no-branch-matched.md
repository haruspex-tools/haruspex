# The null rule `no-branch-matched`

A null rule, `IrNullRule.NoBranchMatched` in the IR's model. A value may be null, or a result missing, because a `CASE` without `ELSE` is null when no condition holds.

## What it means

It is a choice with no branch for when no condition holds (a `CASE` without `ELSE`), so NULL then.

## What to do

To change it, give the `CASE` an `ELSE`.

## What it rests on

Observed against SQL Server in `Case_without_else_choose_out_of_range_and_nullif_of_equal_values_are_null` (`CaseObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
