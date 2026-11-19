# The null rule `not-in-both`

A null rule, `IrNullRule.NotInBoth` in the IR's model. A value may be null, or a result missing, because only one of the two databases returns it.

## What it means

Of a result in a document combining two databases: only one of them returns it, so against the other it is missing.


## What it rests on

There is no engine rule to observe here: it says what haruspex does. `A_trailing_result_set_in_only_one_may_be_missing_against_the_other` (`IrCombinationTests`) shows haruspex giving it.

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
