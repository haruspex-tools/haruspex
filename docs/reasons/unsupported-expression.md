# The unknown reason `unsupported-expression`

An unknown reason, `IrUnknownReason.UnsupportedExpression` in the IR's model. A value or a result is unknown because an expression or function is not modelled yet.

## What it means

An expression or function that is not modelled yet.

## What to do

To change it, declare the results in the config file's `"results"`.

## What it rests on

There is no engine rule to observe here: it says what haruspex does. `A_windowed_aggregate_not_modelled_is_unknown` (`RefinementTests`) shows haruspex giving it.

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
