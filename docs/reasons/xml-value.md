# The null rule `xml-value`

A null rule, `IrNullRule.XmlValue` in the IR's model. A value may be null because XML's value() is null where its path finds no node, or the XML is null.

## What it means

It is read from XML by `.value()` (0.34), which is NULL where its path finds no node, or the XML is NULL. Nothing is modelled
that proves a path always finds a node, so every `.value()` has it, over a column never NULL too.

## What it rests on

Observed against SQL Server in `Xml_value_is_its_type_arguments_type_and_null_where_the_path_finds_no_node` (`XmlObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
