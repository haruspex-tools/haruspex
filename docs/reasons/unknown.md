# The null rule `unknown`

A null rule, `IrNullRule.Unknown` in the IR's model. A value may be null, or a result missing, because its type is unknown, so nothing is proved of it.

## What it means

Its type is unknown, so nothing is proved about it.


## What it rests on

There is no engine rule to observe here: it says what haruspex does. `Fetch_from_a_cursor_this_procedure_does_not_declare_once_assigns_unknown_values` (`DmlTests`) shows haruspex giving it.

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
