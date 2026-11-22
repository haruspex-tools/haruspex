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

**Amended a sixth time** (16 Nov 2026, IR 0.22, [decision 0059](0059-a-query-file-s-parameters-are-typed-from-their-uses.md),
[decision 0060](0060-a-call-returns-its-one-result-itself.md)). The 0.22 reader reads 0.22 only. 0.22 added query files, which a
0.21 file could only lack, but also `returnsCode`, whether a procedure may end with a code other than 0, which no 0.21 file says
and which decides the shape of the procedure's function, so a 0.21 file doesn't mean what a 0.22 file of its source does.

**Amended a seventh time** (18 Nov 2026, IR 0.23, [decision 0070](0070-a-procedure-s-result-sets-are-named-all-or-none.md),
[decision 0071](0071-a-shared-shape-is-named-by-declaration-or-by-its-structure.md)). The 0.23 reader reads 0.23 only. 0.23 added
a result set's `name`, which a 0.22 file could only lack, but also a type's `nameDeclared`, whether the config file gave its name,
which no 0.22 file says and which decides the name a shared shape is declared under, so a 0.22 file doesn't mean what a 0.23
file of its source does.

**Amended an eighth time** (18 Nov 2026, IR 0.24, [decision 0072](0072-a-document-written-with-a-baseline-is-the-combined-one-and-says-so.md)).
The 0.24 reader reads 0.23 too, by the first amendment's test: 0.24 added only what a document combining two databases says
(`source.baseline`, the presence `optional`, the null rule `not-in-both`), and every 0.23 document is a 0.24 document of one
database that means the same. So a document of one database is still written as 0.23, and a file labelled 0.23 that says any of
the three is refused by its path, since 0.23 could say none of them.

**Amended a ninth time** (19 Nov 2026, IR 0.26, [decision 0077](0077-the-ir-carries-a-table-type-s-definition.md)). The 0.26 reader
reads 0.25, 0.24 and 0.23 too, by the same test: 0.26 added only what a document that defines a table type says (`tableTypes`, a
parameter's `tableType`, the type owner `tableType`), and every 0.25, 0.24 and 0.23 document is a 0.26 document without one that
means the same. So a document is labelled with the oldest version that says all it says, and a label that disagrees is refused by
the path of what disagrees.

**Amended a tenth time** (19 Nov 2026, IR 0.27, [decision 0081](0081-binary-values-are-a-kind-of-their-own-counted-in-bytes.md)).
The 0.27 reader reads 0.26, 0.25, 0.24 and 0.23 too, by the same test: 0.27 added only the scalar kind `bytes`, and every document
of those versions is a 0.27 document that holds no binary value, meaning the same. A binary value in a document labelled before
0.27 is refused by its path, and a document labelled 0.27 that holds none is refused by its label.

**Amended an eleventh time** (21 Nov 2026, IR 0.28, [decision 0093](0093-a-column-s-json-is-part-of-its-type-completed-by-the-config.md)).
The 0.28 reader reads 0.27, 0.26, 0.25, 0.24 and 0.23 too, by the same test: 0.28 added only what a column declared to hold JSON
of a schema brings, and every document of those versions is a 0.28 document that declares nothing, meaning the same. What only a
declaration gives in a document labelled before 0.28 is refused by its path, and a document labelled 0.28 that declares nothing is
refused by its label.

**Amended a twelfth time** (22 Nov 2026, IR 0.29, [decision 0097](0097-a-path-into-json-of-known-shape-is-walked-step-by-step.md)).
The 0.29 reader reads 0.28, 0.27, 0.26, 0.25, 0.24 and 0.23 too, by the same test: 0.29 added only a reason's `detail`, what makes
it so in words, and a minimum length's `unit` for a minimum in code points, and every document of those versions is a 0.29 document
with neither, meaning the same (a minimum that says no unit counts UTF-16 code units, as it always did). Either in a document labelled
before 0.29 is refused by its path, and a document labelled 0.29 that has neither is refused by its label.

**See also:** [The IR as a contract](../internals/10-the-ir-as-a-contract.md), "A version the reader doesn't read";
[`tests/ir-versions`](../../tests/ir-versions/README.md).
