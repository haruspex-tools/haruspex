# The null rule `count-overflow`

A null rule, `IrNullRule.CountOverflow` in the IR's model. A value may be null, or a result missing, because `COUNT` is an `int`, null past 2,147,483,647 rows when the caller's session has `ANSI_WARNINGS` and `ARITHABORT` off.

## What it means

It is a `COUNT`, an `int`, which over more than 2,147,483,647 rows overflows: an error by default, but NULL when the caller's session has `ANSI_WARNINGS` and `ARITHABORT` off, which a procedure cannot prevent. `COUNT_BIG` does not.

## What to do

To change it, use `COUNT_BIG`, or declare the callers' `ANSI_WARNINGS` or `ARITHABORT` on in the config file's `"session"`.

## What it rests on

Observed against SQL Server in `Count_over_more_rows_than_int_holds_is_null_under_the_callers_ansi_warnings_and_arithabort_off` (`RefinementObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
