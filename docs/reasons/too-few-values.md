# The null rule `too-few-values`

A null rule, `IrNullRule.TooFewValues` in the IR's model. A value may be null, or a result missing, because `STDEV` and `VAR` are null over fewer than two values.

## What it means

It is a sample statistic, `STDEV` or `VAR`, which is NULL over fewer than two values that are not NULL: a group or a window's frame of one row, with `GROUP BY` too. The population forms, `STDEVP` and `VARP`, are 0 over one value.


## What it rests on

Observed against SQL Server in `The_statistical_aggregates_are_float_and_the_sample_forms_null_over_fewer_than_two_values` (`NumericObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
