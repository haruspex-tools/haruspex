# Binary values are a kind of their own, counted in bytes

**Date:** 19 Nov 2026

**Decision.** The IR has a scalar kind for a string of bytes, `bytes`, which SQL Server's `binary(n)`, `varbinary(n)`,
`varbinary(max)` and `rowversion` (its old name `timestamp`) are. Its only facet is its length, `length` for a fixed one and
`maxLength` for a most, counted in bytes with no encoding, since the bytes are the value and encode no text: `binary(32)` is
exactly 32 bytes (a nullable `binary(32)` column at most 32, since one created with `ANSI_PADDING` off cuts trailing zeros), `varbinary(8000)` at most 8,000, `rowversion` exactly 8, and `varbinary(max)` has none. It has no format, no
minimum length, no digits, no bounds and no value set, and a reader refuses a binary value that says any of them. A document that
holds a binary value anywhere (a parameter, a result, a field or a table type's column, however deeply nested) is IR 0.27, and one
that holds none is labelled as before, by the rest of what it says (decision 0072's labelling by content); a reader of 0.27 reads
all five readable versions and refuses a label that disagrees with what the document holds. What a binary value looks like on the
wire is each target's, as for every kind: the Node `Buffer` the mssql driver hands over in a rowset, and base64 text in JSON, which
[decision 0082](0082-a-binary-value-is-a-buffer-in-a-rowset-and-base64-in-json.md) gives.

`image`, deprecated since SQL Server 2005 and still found in older schemas, is a binary value too, of no length: the driver hands
it over as a `Buffer`, and `FOR JSON` writes it as base64, as it does `varbinary(max)` (observed:
`An_image_value_arrives_as_varbinary_max_s_does`).

**Context.** Before this, the binary types had no kind, so every `binary`, `varbinary` and `rowversion` column, parameter and
expression was an unknown (`unsupported-sql-type`), and so was a binary literal, though the front end already read its type. A team storing a checksum, a thumbnail or a row's version
had nothing checked of it. Bytes are not text: a length counts bytes whatever the collation, a value is compared byte by byte,
and no target writes one as a JSON string of its characters.

**Alternatives.**
- *A string with a format, `base64`.* JSON writes a binary value as base64 text, but a rowset doesn't: the driver hands over a
  `Buffer`, as it hands over a `Date` for a date, which is why dates are kinds and not strings. A string's facets (a length in
  UTF-16 code units, a value set matched under a collation) would all be wrong for it, and every emitter that reads a string's
  length would need to know to ignore it.
- *A length in characters of the base64 text.* That is the JSON form's, and the emitters derive it (four characters for every
  three bytes or part of three); the IR keeps the database's fact, which a rowset checks directly.
- *Leave `image` unknown, as `text` and `ntext` are.* Its values arrive exactly as `varbinary(max)`'s do, so an unknown would
  throw away a fact the driver and `FOR JSON` both guarantee. The expressions that take it are few (it can't be compared, sorted or
  grouped), and those that make a binary value of another one are typed for `binary` and `varbinary` only, so an expression over
  `image` stays unknown.
- *Label every document 0.27.* Every committed IR and every module's header would change in a run that meets no binary value, and
  a 0.26 reader would refuse files it reads correctly.

**Reason.** A kind of its own keeps the IR's rule that a kind says how a value arrives in each encoding, so each emitter can check
it exactly, and keeps a binary value's length out of the rules for text, which are about characters. Labelling by content keeps
every document without one as it was.

**In the code:** `IrScalarKind.Bytes`, `IrDocument.Version`, `IrDocumentScalars`, `IrJsonReader`, `IrJsonSchema`; in the SQL
Server front end, `SqlTypes`, `SchemaRules.TypeFacts`, `SchemaRules.IsNeverNull`, `TypeChain`, `Conversions.Table` and
`Typer.HashBytes`, whose rules the inference README's "Binary values" gives; `IrJsonReaderTests.Bytes.cs`, `IrVersionTests`,
`IrJsonSchemaTests`, `BinaryTests`, `BinaryObservationTests` and the fixture `tests/fixtures/gallery`.
