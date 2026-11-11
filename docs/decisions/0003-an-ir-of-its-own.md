# An intermediate representation of its own

**Date:** 7 Nov 2026

**Decision.** Inference produces a small, closed, versioned format of haruspex's own (the IR), written as deterministic
JSON. Emitters read only the IR.

**Context.** Something has to sit between the SQL front end and the code that is generated. JSON Schema is the obvious
candidate, since it is standard and other tools read it.

**Alternatives.**
- *JSON Schema as the IR.* It is an open validation language (`oneOf`, `allOf`, `if`, `$ref` anywhere), so every emitter
  would have to handle all of it, and it has no notion of procedures, parameters, several result sets, where a claim came
  from, or unknowns, each of which would become a private keyword.
- *No IR*: emitters reading inference's objects in process. Each emitter would then depend on the SQL Server layers, and a
  second dialect or an emitter written in another language would have nothing to read.

**Reason.** A closed format with a node per idea is easy to consume completely and to check, and it maps to JSON Schema
without loss when that is wanted. Protocol Buffers' descriptors, Smithy and TypeSpec make the same choice. Being a file,
the IR is also the contract between the front end and the emitters, and between a database team and the applications that
call it.

**See also:** [`src/Haruspex.Ir/README.md`](../../src/Haruspex.Ir/README.md), "Why not JSON Schema".
