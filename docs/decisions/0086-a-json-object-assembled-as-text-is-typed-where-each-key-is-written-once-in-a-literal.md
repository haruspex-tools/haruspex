# A JSON object assembled as text is typed where each key is written once in a literal

**Date:** 20 Nov 2026

**Decision.** A JSON object built by joining text (`'{"id":' + … + ',"status":"' + string_escape(…, 'json') + '"}'`, its
`concat` form, `'{' + concat_ws(',', '"id":' + …, …) + '}'`) is typed where it is read as JSON, by the rules of decision 0084
for its values, and only where the server guarantees its keys: each key is written whole in a literal, and decoded as JSON
decodes it, so `"a\"b"` is the key `a"b`. A key that may be written twice on one way through the text, as two literals or two
items of `concat_ws` that may both write it, makes the object unknown. Arrays and objects may open in one piece and close in
another, each inside the other: the reader keeps a stack of those open, and text whose brackets don't balance on a way is not
JSON on that way. An item of `concat_ws` that may be NULL is left out, so its keys are keys left out when null, for the item's
null reasons; a key that may also be JSON null, or an item that may be empty text, can't be said by one presence, so the object
is unknown. A value that may be NULL leaves the whole text NULL under `+` and `{"a":}` under `concat`, which is not JSON, so that
way describes no call; `coalesce(x, 'null')` writes the key with a JSON null. Text after the closing brace is unknown.

**Context.** Objects assembled as text are as common in the public corpora as arrays, and a real application builds them inside
`string_agg`. Each point was observed (`JsonAssembledObservationTests.Objects.cs`). Above all, a key written twice is kept twice
by `json_query`, in `FOR JSON`, in `json_object` and in a variable, and readers then differ: `JSON_VALUE` takes the first value,
`OPENJSON` both, and `JSON.parse`, which the generated code runs, the last. The type of one write is not the key's.

**Alternatives.**
- *Take a repeated key's type from its last write*, as `JSON.parse` does. Rejected: it depends on the client's parser, and the
  same text read back by the server (`JSON_VALUE`) gives the other value.
- *Join the types of a repeated key's writes.* Sound for `JSON.parse`, but text a schema accepts would then hold a key twice,
  which no JSON constructor writes and parsers handle differently; the object is better unknown, and the note says how to build it.
- *A new presence for a key that may be left out and may be null.* The IR says it only for a document combining two databases
  (decision 0072); one database's documents keep two presences, and the rare key that needs both is unknown rather than an IR change.
