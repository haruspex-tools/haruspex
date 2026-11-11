# Procedures are selected per schema

**Date:** 9 Nov 2026

**Decision.** `haruspex.json` selects procedures in a block per schema: `include` patterns over the procedures' own names,
then `exclude`, which wins. Nothing is selected unless its schema has a block with an `include`, and a pattern or schema key
that matches nothing is an error.

**Context.** In most databases most procedures write or administer, and only some are an interface worth typing. The first
design took patterns over two-part names (`sales.get_*`) at the top level.

**Alternatives.**
- *Two-part patterns at the top level.* People think "in this schema, these procedures", and a schema's own settings (a
  prefix for its names) had nowhere to live.
- *Every procedure by default.* Typing everything is something to ask for (`"*"`), since being selected is a claim that a
  procedure is an interface.

**Reason.** The block matches how people describe the interface, carries the schema's naming, and needs no syntax for
schema names that contain dots. Reporting a pattern that matches nothing catches a typo that would otherwise drop an
interface from the generated code while CI stays green.

**In the code:** `ProcedureSelection`; [docs/configuration.md](../configuration.md).
