# The null rule `precision-loss`

A null rule, `IrNullRule.PrecisionLoss` in the IR's model. A value may be null, or a result missing, because a decimal rounded to fewer places is null under a caller's `NUMERIC_ROUNDABORT` on with `ANSI_WARNINGS` and `ARITHABORT` off.

## What it means

It is a decimal value that may be rounded to fewer places than it has: a conversion to a type of fewer places, an assignment that converts so, values brought to a common type of fewer places, arithmetic whose result keeps fewer places than its operands need, an average. In SQL Server that rounds silently by default, but with `NUMERIC_ROUNDABORT` on it is an error, NULL when the caller's session also has `ANSI_WARNINGS` and `ARITHABORT` off, and NULL under `TRY_CAST` and `TRY_CONVERT` whatever those are: a session a procedure cannot prevent.

## What to do

To change it, declare the callers' `NUMERIC_ROUNDABORT` off in the config file's `"session"`.

## What it rests on

Observed against SQL Server in `Numeric_roundabort_on_makes_a_loss_of_precision_null_when_ansi_warnings_and_arithabort_are_both_off` (`SessionObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
