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

**Amended** (14 Nov 2026, [decision 0038](0038-the-callers-session-is-declared.md)). The reader also reads 0.16, the one version
every file of which is a file of the current version that means the same: 0.17 added only `source.session`, which a file
leaves out when nothing is declared, and 0.16 assumed nothing about the callers. Nothing is mapped or guessed, so neither
reason above applies. `IrDocument.ReadableVersions` lists what a reader reads; any other version is refused as before.

**Amended again** (14 Nov 2026, IR 0.18). The exception has ended: the 0.18 reader reads 0.18 only. 0.18 added the null rule
`precision-loss`, because 0.17, with nothing declared, claimed never null a decimal that a caller with `NUMERIC_ROUNDABORT` on
gets as NULL ([decision 0038](0038-the-callers-session-is-declared.md)). A 0.16 or 0.17 file has no property 0.18 lacks, but its
claims are not 0.18's, so reading it would carry an older engine's claims under the current label, the first reason above.

**Amended a third time** (14 Nov 2026, IR 0.19). The 0.19 reader reads 0.18 too, by the first amendment's test: 0.19 added
`source.package`, `source.sha256` and `source.references`, which a file read from a folder leaves out, and the unknown reason
`encrypted-definition`, which only a package can give. Nothing inferred from a folder changed, so every 0.18 file is a 0.19
file that names no package and means the same. A file labelled 0.18 that names a package or gives the new reason is refused,
by its path, since 0.18 could do neither.

**Amended a fourth time** (14 Nov 2026, IR 0.20, [decision 0043](0043-a-value-set-is-canonical-by-a-fold-observed-from-the-server.md)).
The 0.20 reader reads 0.20 only. 0.20 added the value match `canonical`, which a set read under a proven collation has where 0.19
said `collation`, from a folder as from a package; so a 0.19 file claims less than a 0.20 file of the same source, and reading it
would carry the weaker claim under the newer label, the first reason above.

**Amended a fifth time** (15 Nov 2026, IR 0.21, [decision 0046](0046-a-sqlcmd-variable-in-a-body-is-unknown.md)). The 0.21
reader reads 0.21 only. 0.21 added the unknown reason `sqlcmd-variable`, for a literal holding a SQLCMD variable, which every
earlier version typed by its written text, though a deployment replaces it. A 0.20 file of such a source claims more than the
server guarantees, and a 0.20 file doesn't say whether its source had one, so reading any of them as 0.21 could carry that claim
under the newer label, the first reason above.

**See also:** [The IR as a contract](../internals/10-the-ir-as-a-contract.md), "A version the reader doesn't read";
[`tests/ir-versions`](../../tests/ir-versions/README.md).
