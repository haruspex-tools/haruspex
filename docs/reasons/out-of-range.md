# The null rule `out-of-range`

A null rule, `IrNullRule.OutOfRange` in the IR's model. A value may be null, or a result missing, because a function given an index, code or position outside what it reads gives null.

## What it means

It is given an argument outside the range it reads, an index, a code, a position, a count or a length, for which it gives NULL rather than an error: a `CHOOSE` whose index may be below 1 or above its count of values, or a string function such as `CHAR` of a code past 255, `ASCII` of an empty string, or `STUFF` at a start past its string's end.


## What it rests on

Observed against SQL Server in `Char_is_char_1_and_null_outside_0_to_255_and_overflows_past_int` (`FunctionObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
