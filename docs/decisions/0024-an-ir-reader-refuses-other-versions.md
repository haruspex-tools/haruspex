# An IR reader refuses a version other than its own

**Date:** 12 Nov 2026

**Decision.** `IrJsonReader` reads the file's `irVersion` before anything else, and refuses a file of any version but
`IrDocument.CurrentVersion`, older or newer, with a message naming the file's version, the version it reads and the path
(`$.irVersion`). It doesn't migrate an older file. It also refuses, by path, anything else the format doesn't allow: an
unknown property, a name outside its vocabulary, a missing or mistyped property, a flag that disagrees with its reasons.
Nothing is read with a default.

**Context.** The IR is at 0.x and has had sixteen versions. Some changes renamed things; others changed what a
claim means or how it is made. A reader has to do something with a file from another version.

**Alternatives.**
- *Migrate older files*, mapping each version's differences onto the next. A rename can be mapped; a change of meaning
  can't, so a migrated file would carry an older engine's claims under the current version's label. Each step is another
  reader to test, kept forever, for files that are build outputs: the source that made them gives a current file in one
  command.
- *Read any version, ignoring what isn't understood*, as Protocol Buffers ignore unknown fields. A property added in a
  later version can weaken a claim (`itemsNullable` says an array's items may be null), so ignoring it reads a stronger
  claim than the file makes: the one thing haruspex must never do.

**Reason.** Regenerating is cheap and always right; migrating is costly and can be silently wrong. A refusal that says
which command to run costs the reader one step. The decision is for 0.x; a stable 1.0 will need its own rule for which
additions a reader may skip.

**See also:** [The IR as a contract](../internals/10-the-ir-as-a-contract.md), "A version the reader doesn't read";
[`tests/ir-versions`](../../tests/ir-versions/README.md).
