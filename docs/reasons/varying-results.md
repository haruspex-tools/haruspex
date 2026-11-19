# The unknown reason `varying-results`

An unknown reason, `IrUnknownReason.VaryingResults` in the IR's model. A value or a result is unknown because which result sets it returns depends on the path in a way a list can't say.

## What it means

Result sets that cannot be listed because which are returned depends on the path through the procedure in a way the list cannot express: a result inside a loop, or results of different encodings at one position.

## What to do

To change it, declare the results in the config file's `"results"`.

## What it rests on

There is no engine rule to observe here: it says what haruspex does. `A_result_inside_a_loop_makes_the_results_unknown` (`StatementFlowTests`) shows haruspex giving it.

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
