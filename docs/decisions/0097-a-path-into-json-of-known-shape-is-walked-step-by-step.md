# A path into JSON of known shape is walked step by step

**Date:** 22 Nov 2026

**Decision.** A literal path into JSON whose shape haruspex knows, built in the procedure or declared for a column, is read as
SQL Server reads it (`lax` or `strict`, then keys and array indexes) and walked along the type, a step at a time. Each step says
what it finds there, and whether it may find nothing or `null`: a key the schema doesn't require may be left out, a nullable
value may be `null`, an index past an array's fewest items may be past its end, a key of a map may not be one of its keys, a key
an open object doesn't list may hold anything, any JSON may hold anything below it, and a union is walked a variant at a time.
`JSON_VALUE`, `json_query` with a path and `OPENJSON` then apply their own observed rules to what the walk finds, by the path's
mode. The steps that make a value NULL are recorded on its null reason, in words (`detail`, IR 0.29), so that `explain` can say
which step it was. A path the shape can never have (a key a closed object doesn't have, a key of a string) is a warning,
HAR2102. A wildcard, a range, a path that is not a literal or one the reader doesn't read is not walked: `JSON_VALUE` falls back
to its own type (`nvarchar(4000)`, nullable), and `json_query` with a path is unknown, saying why.

**Context.** Once a column's JSON is declared ([decision 0093](0093-a-column-s-json-is-part-of-its-type-completed-by-the-config.md)),
the commonest read of it is a path: `JSON_VALUE(c, '$.address.city')`. The same walk already typed paths into JSON built here,
with two outcomes per step (present, or left out when null), which stored JSON outgrew: a writer may leave a key out whatever its
value, write `null`, or add keys the schema doesn't list.

**Alternatives.**
- *Type a path's result from the schema's node alone.* Simpler, but wrong at every step that can miss: a required key under an
  optional one is not always there, and a lax path gives NULL for nothing there.
- *Read a declared path as the application's claim, like an override.* A path is code, not a claim: what it finds is fixed by the
  declaration and the server's rules, so haruspex can work it out and needn't be told.
- *Say why only in prose, outside the IR.* `explain` reads the IR alone, so the steps would be lost between `generate` and
  `explain`; a detail on the reason costs one optional property, and the document says it only where a path made a value NULL,
  so every other document keeps its version (labelling by content, [decision 0072](0072-a-document-written-with-a-baseline-is-the-combined-one-and-says-so.md)).

**Reason.** The walk claims exactly what the declaration and the server's observed rules guarantee at each step, and nothing at a
step it can't see; the detail lets a reader see which step to change, in the schema or in the SQL.

**In the code:** `JsonPath` (the grammar, as observed), `JsonPathWalk` (the walk), `Typer.JsonReads.cs` (each function's rules),
`IrNullReason.Detail`; the inference README, "Paths into JSON".
