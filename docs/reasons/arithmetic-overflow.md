# The null rule `arithmetic-overflow`

A null rule, `IrNullRule.ArithmeticOverflow` in the IR's model. A value may be null, or a result missing, because arithmetic that overflows or divides by zero is null when the caller's session has `ANSI_WARNINGS` and `ARITHABORT` off.

## What it means

It is arithmetic, a `SUM` or `AVG`, a numeric function (`ROUND`, `ABS`, `POWER`, `EXP`, …) or a conversion that can overflow its type or divide by zero: an error by default, but NULL when the caller's session has `ANSI_WARNINGS` and `ARITHABORT` off, which a procedure cannot prevent. `COALESCE` or `ISNULL` with a value never null removes it.

## What to do

To change it, declare the callers' `ANSI_WARNINGS` or `ARITHABORT` on in the config file's `"session"`, or wrap it in `COALESCE` or `ISNULL`.

## What it rests on

Observed against SQL Server in `An_overflow_is_an_error_by_default_and_null_under_the_callers_ansi_warnings_and_arithabort_off` (`ExpressionObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
