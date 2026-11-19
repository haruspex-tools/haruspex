# The null rule `all-fields-null`

A null rule, `IrNullRule.AllFieldsNull` in the IR's model. A value may be null, or a result missing, because it is a nested object left out when every one of its values is null.

## What it means

It is a nested object that is left out when every one of its fields is null.


## What it rests on

Observed against SQL Server in `A_nested_object_whose_values_are_all_null_is_left_out_or_with_include_null_values_written_with_nulls` (`ForJsonObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
