# The unknown reason `unsupported-query`

An unknown reason, `IrUnknownReason.UnsupportedQuery` in the IR's model. A value or a result is unknown because a query form is not modelled yet.

## What it means

A query form that is not modelled yet, such as a CTE or `UNION`.

## What to do

To change it, declare the results in the config file's `"results"`.

## What it rests on

There is no engine rule to observe here: it says what haruspex does. `A_query_form_not_modelled_makes_the_result_unknown` (`ConstructTests`) shows haruspex giving it.

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
