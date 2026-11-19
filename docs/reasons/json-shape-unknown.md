# The unknown reason `json-shape-unknown`

An unknown reason, `IrUnknownReason.JsonShapeUnknown` in the IR's model. A value or a result is unknown because the JSON's shape is in the data, not the code.

## What it means

JSON whose shape is not visible here, such as JSON text read from a column.

## What to do

To change it, type it in the config file with `"type"` and `"import"`.

## What it rests on

There is no engine rule to observe here: it says what haruspex does. `A_json_column_is_live_json_of_unknown_shape_with_the_column_s_nullability` (`JsonConstructorTests`) shows haruspex giving it.

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
