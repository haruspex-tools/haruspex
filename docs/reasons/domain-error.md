# The null rule `domain-error`

A null rule, `IrNullRule.DomainError` in the IR's model. A value may be null, or a result missing, because a math function outside its domain is null when the caller's session has `ANSI_WARNINGS` and `ARITHABORT` off.

## What it means

It is a math function that may be given a value outside its domain: `SQRT` of a negative, `LOG` or `LOG10` of 0 or less or to a base of 0 or 1, `POWER` of a negative to a fractional power. That is an invalid floating point operation: an error by default, but NULL when the caller's session has `ANSI_WARNINGS` and `ARITHABORT` off, which a procedure cannot prevent, as for `IrNullRule.ArithmeticOverflow`.

## What to do

To change it, declare the callers' `ANSI_WARNINGS` or `ARITHABORT` on in the config file's `"session"`, or wrap it in `COALESCE` or `ISNULL`.

## What it rests on

Observed against SQL Server in `A_math_function_outside_its_domain_is_an_error_by_default_and_null_under_the_callers_options` (`NumericObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
