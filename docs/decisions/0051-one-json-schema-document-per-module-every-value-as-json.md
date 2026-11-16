# One JSON Schema document per module, every value as JSON writes it

**Date:** 16 Nov 2026

**Decision.** The JSON Schema emitter writes one document per module, `haruspex.jsonschema.json` beside the IR, in draft
2020-12. Its `$defs` holds a definition for each procedure's results, under the procedure's generated name, and one for each
named object type, under its name, sorted by name; references between them, recursive ones included, are `$ref`s within the
document. A consumer finds a procedure's schema by its name, as the Zod module exports it: `haruspex.jsonschema.json#/$defs/GetOrder`
(in Ajv, `ajv.getSchema("haruspex.jsonschema.json#/$defs/GetOrder")` after adding the document under that key). The
definition's `title` is the procedure's name in the database, `shop.get_order`, so a tool listing the definitions can show
both. The root schema has no assertions of its own: it is the container, and its `description` says how to find a
procedure's schema and what every value is described as.

Every value is described as JSON, as SQL Server's `FOR JSON` writes it: a `FOR JSON` result as the document the mssql driver
hands over ([decision 0048](0048-an-absent-json-result-reads-as-null.md)), and a rowset as an array of its rows, each the object
`FOR JSON PATH, INCLUDE_NULL_VALUES` writes for it: every column a key, always there, null where the column is null (plain
`FOR JSON` would leave a null column's key out, which a rowset never does), and each value written as `FOR JSON` writes it, but
for JSON text: a column holding JSON, the native `json` type included, is the string it holds, as every driver hands it over and
as the IR claims it (`json-text`), where `FOR JSON` would embed a `json` column (dates, times and GUIDs as strings in fixed formats, every number, a
`bigint` included, as a JSON number).

**Context.** JSON Schema is the second emitter, from the IR alone. It serves teams in any language with a JSON
Schema validator, and is what later work builds on: conformance (comparing what a procedure returns with a team's own
schema) and catalogues for MCP. JSON Schema describes JSON values. A rowset is not JSON: each driver hands it over in its
language's own types (the mssql driver's `Date` and a `bigint` as a string; Python's drivers a date and time object and an exact decimal), so the
document has to say which JSON a row is.

**Alternatives.**
- *A file per procedure*, each with the named types it reaches. One procedure's schema is then a file to point at, but a
  type shared by several procedures is written into each, so two files can disagree about one type's name, and a recursive
  type would be repeated wherever it is reached. One document keeps one definition per name, as the IR and the Zod module do,
  and a JSON pointer is how JSON Schema already addresses a part of a document.
- *A root that is the union of every procedure's results*, or an object keyed by procedure. Neither is a schema anyone
  validates a value against: a value is one procedure's result, and the consumer knows which.
- *Separate sections for procedures and types* (`$defs/procedures/…`). The config file already settles every name so that
  none clashes (a clash is an error, [decision 0011](0011-generated-names-leave-the-schema-out.md)), so one namespace is
  enough and the pointer is short; an IR from elsewhere that gives one name twice is refused (HAR5101).
- *Rows as the mssql driver delivers them, serialised by `JSON.stringify`*: dates as `Date.toISOString()` (a `date` would
  become a UTC midnight, a time a day in 1970), a `bigint` as a string. That is one driver's JavaScript, which the Zod module
  already describes exactly; the JSON Schema serves the other languages, whose drivers each differ. `FOR JSON`'s form is the
  server's own, observed and kept as tests, and the same in every language.

**Reason.** One document is one definition per name, addressed the way JSON Schema addresses anything; and describing every
value as `FOR JSON` writes it gives one meaning to every schema in the document, defined by the server rather than by a
driver.

**In the code:** `JsonSchemaPlanner` (the definitions and their order), `JsonSchemaLowering` (the document's description),
`Scalars` (each kind's JSON form), and the differential test `JsonSchemaAcceptanceTests`, which writes each observed row in
that form and validates it.
