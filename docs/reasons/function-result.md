# The null rule `function-result`

A null rule, `IrNullRule.FunctionResult` in the IR's model. A value may be null, or a result missing, because it is a function's result, typed from its declared return type, which may be null.

## What it means

It is a scalar function's result, typed from the declared return type only, so it may be NULL.

## What to do

To change it, if the data rules it out, declare it in the config file with `"nullable": false` and why.

## What it rests on

There is no engine rule to observe here: it says what haruspex does. `A_function_of_another_type_gives_its_declared_type_and_may_be_null` (`JsonFunctionTests`) shows haruspex giving it.

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
