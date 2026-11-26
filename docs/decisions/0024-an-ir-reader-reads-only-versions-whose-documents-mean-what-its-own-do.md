# An IR reader reads only versions whose documents mean what its own do

**Date:** 12 Nov 2026; restated 23 Nov 2026 as the rule stands at IR 0.30, 25 Nov 2026 for 0.31, and 26 Nov 2026 for 0.32.

**Decision.** `IrJsonReader` reads a file's `irVersion` before anything else. It reads the current version, and an older one only
when every document of that version is a document of the current version that means the same (`IrDocument.ReadableVersions`).
Any other version, older or newer, is refused with a message naming the file's version, the versions the reader reads and the
path (`$.irVersion`). Nothing is migrated. A writer labels a document with the oldest version that says all it says
(`IrDocument.Version`), and the reader refuses, by the path of what disagrees, a label that says more or less than its document.
Anything else the format doesn't allow is refused by its path too, and nothing is read with a default.

**Context.** The IR is at 0.x, and most changes to the format have come with a new version. Some only added something a document
may lack; others changed what a claim means, or which rules made it, so that the same source gives another claim. A reader has to
do something with a file of another version. Before 0.30 the file doesn't say which rules made its claims; from 0.30 it names the
build that wrote it ([decision 0117](0117-an-ir-document-names-the-build-that-made-its-claims.md)), and a version says only which
properties a document uses.

**The test.** An older version is read only if a document of it, read as the current version, claims exactly what it claimed when
it was written. Two cases show the two answers:

- *0.19 read 0.18.* 0.19 added what a package gives (the package a source was read from, its hash, the packages it refers to, and
  the unknown reason `encrypted-definition`), which a document inferred from a folder leaves out. Nothing inferred from a folder
  changed, so every 0.18 document is a 0.19 document that names no package and means the same.
- *0.20 refused 0.19.* 0.20 made a value set read under a collation proven against the server `canonical`, where 0.19 said
  `collation` of the same source, a weaker claim. Read as 0.20, a 0.19 document would carry the weaker claim under a label that
  promises the stronger one.

0.20 is history: a change to a rule moved the version then, in the safe direction, since the older document claimed less. A
correction can go the other way ([decision 0095](0095-a-length-is-the-server-s-and-a-target-claims-the-fewest-code-points-it-can-hold.md):
older files claimed more than is true, under a label no fix moved). From 0.30 a correction to a rule doesn't move the version, and
the build that wrote a document records it ([decision 0117](0117-an-ir-document-names-the-build-that-made-its-claims.md)); the
version moves when the format does, a property or a value added or what one means changed, and this test decides which older
versions are read.

Every version from 0.24 to 0.32 added only what a document either has or lacks (a baseline, where a value came from, table types,
binary values, declarations of a column's JSON, a reason in words, the build that wrote it, the packages declared builds, a
source's several folders), so the 0.32 reader reads each of them
and 0.23. No 0.22
document says whether the config file named a type, which 0.23 added and which decides the name a shared shape is given, so 0.22
is refused. [`tests/ir-versions`](../../tests/ir-versions/README.md) keeps a file of every version and says which are read.

Labelling by content is the test's other half. A document that says nothing a version added is written with the earlier label,
byte for byte as before, so an older reader, haruspex's own or another program's, still reads it; only a document that needs the
newer reader says so ([decision 0072](0072-a-document-written-with-a-baseline-is-the-combined-one-and-says-so.md)).

**Alternatives.**
- *Read only the current version.* The rule as first written. It refuses files an older and a newer reader understand alike, and
  makes every run that adds nothing new rewrite the label of every committed file.
- *Migrate older files*, mapping each version's differences onto the next. A rename can be mapped; a change of meaning can't, so a
  migrated file would carry an older engine's claims under the current version's label. Each step is another reader to test, kept
  forever, for files that are build outputs: the source that made them gives a current file in one command.
- *Read any version, ignoring what isn't understood*, as Protocol Buffers ignore unknown fields. A property added in a later
  version can weaken a claim (`itemsNullable` says an array's items may be null), so ignoring it reads a stronger claim than the
  file makes: the one thing haruspex must never do.

**Reason.** Regenerating is cheap and always right; migrating is costly and can be silently wrong. Reading a version whose
documents mean the same costs nothing and spares every reader a rewrite. A refusal that says which command to run costs the
reader one step. The decision is for 0.x; a stable 1.0 will need its own rule for which additions a reader may skip.

**See also:** [The IR as a contract](../internals/10-the-ir-as-a-contract.md), "A version the reader doesn't read";
[decision 0038](0038-the-callers-session-is-declared.md), where the exception was first made.
