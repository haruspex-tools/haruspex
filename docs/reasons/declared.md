# The null rule `declared`

A null rule, `IrNullRule.Declared` in the IR's model. A value may be null, or a result missing, because the config file declares it may be.

## What it means

The config file declares it may be null, or absent: in results declared for a procedure whose results cannot be inferred. `IrNullReason.At` is the procedure declared.


## What it rests on

There is no engine rule to observe here: it says what haruspex does. `A_rowset_is_declared_by_the_schema_of_one_row` (`ResultsDeclarationTests`) shows haruspex giving it.

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
