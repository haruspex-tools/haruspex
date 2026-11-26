# The null rule `xml-value`

A null rule, `IrNullRule.XmlValue` in the IR's model. A value may be null because XML's value(), or an OPENXML column, is null where its path finds no node, or the XML is null.

## What it means

It is read from XML by `.value()` (0.34), or as a column of `OPENXML … WITH`, either of which is NULL where its path finds no
node, or the XML is NULL. Nothing is modelled that proves a path always finds a node, so every `.value()` has it, over a column
never NULL too, and so does every `OPENXML` column, even one that a table named in `WITH` declares `NOT NULL`.

## What it rests on

Observed against SQL Server in `Xml_value_is_its_type_arguments_type_and_null_where_the_path_finds_no_node` (`XmlObservationTests`),
and for `OPENXML` in `Openxml_with_columns_are_their_declared_types_converted_as_cast_converts_text` and
`Openxml_with_a_table_s_name_is_null_where_its_path_finds_nothing_whatever_the_table_declares`
(`XmlExpressionObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
