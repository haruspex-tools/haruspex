# The unknown reason `unresolved-recursion`

An unknown reason, `IrUnknownReason.UnresolvedRecursion` in the IR's model. A value or a result is unknown because functions that call themselves reached no fixed point.

## What it means

Functions that call themselves, directly or through others, whose shape reached no fixed point in the bounded number of steps, or that never return a value (every return calls itself again).

## What to do

To change it, declare the results in the config file's `"results"`.

## What it rests on

There is no engine rule to observe here: it says what haruspex does. `Recursion_with_no_fixed_point_in_bounded_steps_is_unknown` (`JsonFunctionTests`) shows haruspex giving it.

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
