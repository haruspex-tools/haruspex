# An IR document names the build that made its claims

**Date:** 23 Nov 2026

**Decision.** From IR 0.30, a document names the build of the tool that wrote it, in `source.writtenBy`: its version as the tool
says it, and the whole commit it was built from when the build knows it (`IrBuild`). Its version label still says which properties
it uses; the build says whose rules made its claims. Every document haruspex writes is 0.30 and names the build that runs. A
combined document names its baseline's writer too: the build that runs, when the baseline is inferred from its source, or the one
the baseline's IR file names, or none when that file names none. The reader requires the build in a document of 0.30 and refuses
it in one labelled earlier, which keeps its old meaning and is still read.

A baseline given as an IR file holds the claims of the build that wrote it. Unless that is the build that runs (the same version
from the same known commit), `generate` and `check` warn (HAR6043), naming both builds, and steer to the source the file was
inferred from, which the build that runs infers again with its own rules.

The build is where the claims came from, not a claim, so nothing that compares claims looks at it: `check` passes an IR another
build wrote whose claims are the same, as [decision 0050](0050-check-compares-claims-not-files.md) has it compare claims and not
files, and `generate` reports such a run as one in which no claim changed. A test whose output is a reviewed snapshot runs a build
that never moves (`FixedBuild`, its commit all zeros), so no snapshot changes with the commit; a test that compares a committed IR
file with what the build makes, as the example application's does, compares it as `check` does.

**Context.** [Decision 0095](0095-a-length-is-the-server-s-and-a-target-claims-the-fewest-code-points-it-can-hold.md) corrected a
claim (a value converted to `nchar(n)` is not always n code units long) without changing the format, so a file labelled 0.25 to 0.28
written before it and one written after it share a label and say different things of the same source. Other soundness fixes did
the same. Moving the version could not have told them apart: the label records which properties a document uses, and the
correction added none. Read as a baseline, the earlier file carried the withdrawn claim into new types without a word.

So a label before 0.30 says only which properties a document uses. A file labelled 0.25 to 0.29 may hold claims that later builds
corrected without moving the version; the IR README's changelog says so under 0.30.

**Alternatives.**
- *A rule-set revision (`source.rules`), raised by every soundness fix.* It names exactly what matters, but only if every fix
  remembers to raise it, which no test can check; a build's commit is recorded by the build itself and is never forgotten.
- *Move the version with every soundness fix.* Files written within one version before and after a fix would still share a label,
  and every reader of the format would be told the format changed when it hadn't.
- *Refuse a baseline written by another build.* Safe, but it stops a team whose committed baseline came from the last release, where
  a warning that names both builds and the command that removes the doubt lets them decide.
- *Compare the build in `check`, or keep it in snapshots.* Every commit to haruspex would turn the check of an unchanged module red,
  and every reviewed snapshot would change though no claim did.
- *A time of writing.* A run would no longer give the same bytes twice, which the IR's writer promises.

**Reason.** The build is the one fact that names whose rules made a claim and that no one has to remember to update. A reader that
holds it can tell a file of its own claims from one of another's, and the files that need no such judgement (the module, the
snapshots, `check`'s comparison) stay the same from commit to commit.

**Limits.** A build made from changes not yet committed names the commit they were made on. A build made without its commit (from a
`git archive` copy or a source tarball) names its version alone, which is true of it, and so can't tell a file it wrote from one
another such build wrote: it warns of both.

**In the code:** `IrBuild`, `IrSource.WrittenBy`, `IrDocument.CurrentVersion`; `Terminal.Build` and `Terminal.BuildOf` in the
command line, `ModuleRun`'s inference, and `Baseline`, where the baseline is read; the IR README's "Versioning" and its 0.30 changelog.
