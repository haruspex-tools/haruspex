# XML is typed from its schema collection where the schema proves it

**Date:** 26 Nov 2026

**Status:** the nulls, the rows and the reading of the schemas are built. The facets wait for the IR's change, below, which the
coordinator has before it is made.

**Decision.** A value read from a typed `xml` instance by `value()`, `exist()` or `nodes()` is typed from the instance's schema
collection where the collection proves a fact. SQL Server validates a typed instance on every write, so what its schemas
constrain holds of every stored value. Every rule below was observed on SQL Server 2025 CU10.

- **Which instances.** A table's column, a variable or a parameter declared `xml(collection)`, and a column that `nodes()` gives
  over one of these. Any other `xml` value (a view's column, a conversion, `query()`) is read as before.
- **The schemas.** Every schema in the collection, as `CREATE` and each `ALTER … ADD` give them, compiled together by .NET's XML
  Schema compiler in Sources into the model's declarations, which Semantics follows: Model, Semantics and Inference never read XML
  (docs/architecture.md). A collection whose text isn't known, or that the compiler refuses, proves nothing.
- **Paths.** Only a path of named child elements, perhaps ending in an attribute, each step with or without `[1]`, or the whole
  path in parentheses followed by `[1]`; with the prolog's `declare namespace` and `declare default element namespace`. A batch
  that uses `WITH XMLNAMESPACES` proves nothing, since its prefixes and default namespace aren't read.
- **The root.** An absolute path starts at the instance's top level. A `DOCUMENT` instance has exactly one top-level element,
  any global element of any schema in the collection, so a top-level step is required only when the collection declares exactly
  one global element that isn't abstract. A `CONTENT` instance may have none, or many, so no top-level step is required and none
  is bounded. A path from a node that `nodes()` gives starts at that node, which is there.
- **Required.** A child element is required when its parent's content model makes it occur at least once (sequences add, a choice
  takes its least branch, each scaled by the group's `minOccurs`). A nillable element may be `xsi:nil` and then has no children, so no step below it is required,
  though its own attributes still are (observed). An attribute is required when its use is `required`, or it has
  a default or a fixed value, which the server writes into the instance (observed).
- **`value()`** over a path whose every step is required is non-null where the instance is, unless its last node is a nillable
  element (`xsi:nil` gives NULL: observed). Its node must be an attribute or an element of a simple type or simple content; the
  server refuses other content (9314).
- **Facets.** The last node's simple type's facets, through every restriction it derives by, and the bounds of the built-in
  integer types, are given to the result where the conversion keeps them. The server converts the value's canonical text as
  `CAST` converts `nvarchar`, and requires `ANSI_WARNINGS` for XML's methods (1934), so a bad value is an error, never NULL
  (observed). To an integer type every bound and enumeration holds exactly, since text that isn't a whole number fails (245). To
  `decimal(p,s)` text is rounded to `s` places, so a bound holds exactly only when the values have no more places (an integer type,
  or `fractionDigits` no more than `s`); otherwise it is widened to an inclusive bound at `s` places. To `nvarchar(n)` a string type's
  `length`, `minLength`, `maxLength` and enumeration hold, cut to `n` as the string is; the server counts them in UTF-16 code units
  (observed), as `nvarchar` does. Patterns, other target types, `xs:float`, `xs:double`, dates and lists or unions carry nothing.
- **`nodes()`** rows are bounded by the occurrences along its path, so at least one row and at most one where the schema says so.
- **`exist()`** on a path whose every step is required is 1 where the instance is non-null.
- **Cautious.** Nothing is proved through an element whose type the collection derives another type from (`xsi:type` may choose
  that type: observed), through an element that heads a substitution group or is abstract (a member stands in its place: observed),
  through a content model with an element wildcard (a wildcard may match an element of the same name before the declared one:
  observed), nor through `xs:anyType`. An attribute wildcard doesn't stop a declared attribute, which the server still validates
  (observed).

**The IR.** A non-null value and a bounded `nodes()` need no change. A facet needs a source: a new kind of fact source,
`xml-schema`, whose ref names the collection and the declaration (`Sales.StoreSurveySchemaCollection {urn}StoreSurvey/BusinessType`).
A document that holds one is 0.35, and one that holds none is labelled as before (decisions 0072, 0122). `exist()` as 1 would need
a value set on a boolean, which no target reads today.
