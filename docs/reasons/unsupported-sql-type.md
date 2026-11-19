# The unknown reason `unsupported-sql-type`

An unknown reason, `IrUnknownReason.UnsupportedSqlType` in the IR's model. A value or a result is unknown because a SQL type is not modelled yet.

## What it means

A SQL type that is not modelled yet.

## What to do

To change it, declare the results in the config file's `"results"`.

## What it rests on

There is no engine rule to observe here: it says what haruspex does. `A_binary_literal_is_read_as_varbinary_of_its_bytes_a_type_not_modelled_yet` (`ExpressionTests`) shows haruspex giving it.

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
