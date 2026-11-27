# XML is typed from its schema collection where the schema proves it

**Date:** 26 Nov 2026; the IR's part 27 Nov 2026, agreed with the coordinator and provisional until the maintainer settles it at the
merge.

**Decision.** A value read by `value()`, and the rows `nodes()` gives, from an `xml` instance typed by a schema collection are
typed from the collection where its schemas prove a fact. SQL Server validates a typed instance against its collection on every
write, and an `ALTER … ADD` validates every stored instance again, so what the schemas constrain holds of every stored value: a
proof, not a declaration. Every rule below was observed on SQL Server 2025 CU10 (`XmlSchemaObservationTests`).

- **Which instances.** A table's column, a variable or a parameter declared `xml(collection)`, and a column that `nodes()` gives
  over one of these. Any other `xml` value (a view's column, a conversion, `query()`'s untyped result) is read as before.
- **The schemas.** Every schema in the collection, as `CREATE` and each `ALTER … ADD` give them and every `xs:schema` of each
  text, compiled together by .NET's XML Schema compiler in Sources into the model's declarations, which Semantics follows: Model,
  Semantics and Inference never read XML (docs/architecture.md). A collection whose text isn't known, or that the compiler
  refuses, proves nothing.
- **Paths.** Only a path of named child elements, perhaps ending in an attribute, each step with or without `[1]`, or the whole
  path in parentheses followed by `[1]`, after a prolog of `declare namespace` and `declare default element namespace`; each name
  is matched by its namespace and local name together. A batch that uses `WITH XMLNAMESPACES` proves nothing, since its prefixes
  and default namespace aren't read.
- **The root.** An absolute path starts at the instance's top level. A `DOCUMENT` instance has exactly one top-level element, which
  may be any global element of any schema in the collection, so a top-level step is required only when the collection declares
  one global element that isn't abstract. A `CONTENT` instance may have none, or many, so no top-level step is required or
  bounded. A path from a node that `nodes()` gives starts at that node, which is there.
- **Required.** A child element is required when its parent's content model makes it occur at least once: a sequence or an all
  group adds its parts, a choice takes its least branch, each scaled by the group's occurrences. A nillable element may be
  `xsi:nil` and then has no children, so no step below it is required, though its own attributes still are. An attribute is
  required when its use is `required`, or when it has a default or a fixed value, which the server writes into the instance.
- **`value()`** over a path whose every step is required is NULL only where its instance is, if its last node is an attribute, or
  an element of a simple type or simple content that isn't nillable (`xsi:nil` gives NULL). The server refuses `value()` of any
  other content (9314).
- **Facets.** Whatever the path's last node, its simple type's facets, through every type it restricts, and a built-in integer
  type's range, are given to the value where the conversion to `value()`'s type keeps them. The server converts the value's
  canonical text as `CAST` converts `nvarchar`, and XML's methods require `ANSI_WARNINGS` (error 1934), so text the type can't
  take is an error, never NULL. To a whole-number type a number's bounds hold exactly, and its enumeration's whole numbers, since
  other text fails (245). To `decimal(p,s)` the text is rounded half away from zero to `s` places, so a bound holds exactly only
  where the values have no more places (a whole-number type, or `fractionDigits` no more than `s`), and otherwise is widened to the
  inclusive bound at `s` places outside it; an enumeration holds where each item has no more places. To `nvarchar(n)` a string
  type's `length`, `minLength`, `maxLength` and enumeration hold, a length cut to `n` as the value is, an enumeration only where
  `n` holds every item; the server counts XML Schema's lengths in UTF-16 code units, as `nvarchar` does, so they reach Zod by
  decision 0095's path. A bound the type already has keeps the type as its source. Patterns, which no target here checks, other
  target types (`varchar` may change a character to its code page's best fit, `nchar` pads), and other kinds of value (floats,
  booleans, dates, lists, unions) carry nothing.
- **`nodes()`** gives at least one row for each outer row where its path is required and the instance is never NULL, and at most
  one where no step may repeat, so an `OUTER APPLY` over a required path null-extends nothing.
- **`exist()`** stays a `bit`, NULL exactly where its instance is. That it is 1 on a required path would need a value set on a
  boolean, which no target reads today; it is a follow-up.
- **Cautious.** Nothing is proved into an element that heads a substitution group or is abstract (a member stands in its place),
  through an element of a complex type an instance may replace (`xs:anyType`, an abstract type, or one the collection derives
  another type from, which `xsi:type` may name), nor through content with an element wildcard, which may take an element of a
  declared name before the declared one, unvalidated. A simple type another derives from still proves its node, since a derived
  simple type only narrows its values; an attribute wildcard doesn't stop a declared attribute, which the server still validates.

**The IR.** A proof of a non-null value only drops a reason, and a bounded `nodes()` is a cardinality the IR has; neither changes
the IR. A facet needs a source: the fact source `xml-schema`, whose ref names the collection and the node's declaration by the path
to it (`shop.order_schema {urn:shop}order/@id`). A document that holds one is 0.35, and one that holds none is labelled as before,
byte for byte (decision 0072's labelling by content, as decisions 0122, 0127 and 0128 follow). Emitters treat it as a CHECK's: the
JSON Schema note names the collection, and Zod keeps a bigint's bounds from it.

**Context.** Decision 0133 bound each typed `xml` to its collection and read the collections' text so that this could follow; 0137
typed `nodes()`, `query()`, `FOR XML` subqueries and `OPENXML`. Until now every `value()` was nullable and every `nodes()` any
number of rows, though the server enforces what the schema says. AdventureWorks's six typed columns are all `CONTENT`, so its gains
are relative to a `nodes()` row: 10 `value()` results in four views become non-null (each checked against the server with its
own collection).

**Alternatives.**
- *Read the schemas in Semantics with .NET's compiler directly.* Simpler, but Semantics never reads XML; the model holds the
  declarations as data instead, which any analysis can read.
- *Follow `xsi:type` and substitution groups.* Extension keeps the base's content first and restriction narrows it, so some proofs
  would survive, but each needs SQL Server's own derivation rules (`block`, `final`) observed; refused for now, with a reason.
- *Reuse the fact source `type` or `engine`.* No IR change, but either would misdescribe the source: the value's SQL type is the
  type argument's, and the engine enforces the collection, not a closed set of its own.
- *Count lengths in code points.* XML Schema says characters, but SQL Server counts UTF-16 code units (observed), as `nvarchar`
  does.

**Reason.** Each claim is something the server enforces on every write, read only as far as observed, and anything not modelled
keeps the typing of decisions 0133 and 0137, so nothing is claimed that a stored instance could break.
