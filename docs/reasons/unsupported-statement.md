# The unknown reason `unsupported-statement`

An unknown reason, `IrUnknownReason.UnsupportedStatement` in the IR's model. A value or a result is unknown because a statement it runs is not modelled yet.

## What it means

A statement that is not modelled yet, which may return results of its own.

## What to do

To change it, declare the results in the config file's `"results"`.

## What it rests on

There is no engine rule to observe here: it says what haruspex does. `A_statement_not_modelled_anywhere_makes_the_results_unknown` (`StatementFlowTests`) shows haruspex giving it.

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
