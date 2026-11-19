# A document written with a baseline is the combined one, and says so

**Date:** 18 Nov 2026

**Decision.** With a baseline, `haruspex generate` writes the combined document as `haruspex.ir.json`: the one its modules are
written from, whose types hold against both databases ([decision 0073](0073-generate-with-a-baseline-writes-types-that-hold-against-both-databases.md)).
It says it is combined by naming the baseline's source in `source.baseline` (its dialect, and its package and references when
it was read from one). Only a combined document may have a key whose presence is `optional` (left out whatever its value, since
one database doesn't return it, or one leaves it out when null where the other sends the null) or a result missing for the
reason `not-in-both`. IR 0.24 adds these three. A document of one database is still labelled 0.23, since it is a 0.23 document
that means the same: a run without a baseline writes what it wrote before, byte for byte, and a reader of 0.23 still reads it.
A reader of 0.24 reads both, and refuses, by its path, a file labelled 0.23 that says any of the three, and one labelled 0.24
without a baseline. A combined document describes an application, not a database, so `generate` and `check` refuse one as their
baseline (HAR6036), but for `check --fit-only`.

**Context.** The committed IR does two jobs beyond being read back by `haruspex check`. A database's pipeline checks a change
against each application's committed IR, with `check --fit-only`, to refuse a deploy that would break one already running
([decision 0069](0069-check-with-a-baseline-judges-the-deploy-window.md)); and the IR is the interface for backends, which write
code from it alone. Both need it to describe what the application's code accepts, which with a baseline is the combined types.

**Alternatives.**
- *Write the source's own document, and combine only in the emitters.* The IR and the code would disagree: a backend reading the
  IR would write types for one database, and a database's pipeline would judge its change against types narrower than the
  application's, refusing changes the application takes (removing a key it already reads as optional).
- *Write both documents.* A second file to commit and keep in step, for one use, the next run's baseline, which the database's
  own source serves better.
- *Label every document 0.24*, as the format's versions have moved until now. Every module's header and IR would change in a run
  that combines nothing, and a reader of 0.23 would refuse files it reads correctly. A combined document is the one a 0.23 reader
  would misread, and the one it now refuses.
- *Say a key in one database only with the presences there were.* `omittedWhenNull` says a key is left out only when its value
  is null, so it can't say a key that may be missing and may be null, as a nullable column only the new database returns is.
- *Accept a combined document as `generate`'s baseline.* Sound, but its results are already wider than the database's, so types
  combined with it never tighten again as releases go by; and a parameter it left out would be reported as new.

**Reason.** The IR is the contract the application's code was written from, and a contract that described something else would
be read wrongly by whoever trusts it. Labelling a document by what it says keeps every run without a baseline exactly as it was.

**In the code:** `IrSource.Baseline`, `IrDocument.Version` and `IrDocument.OneDatabaseVersion`, `IrPresence.Optional`, `IrNullRule.NotInBoth`,
`IrJsonReader`; `CombinedTypes` in the CLI; `IrCombinedDocumentTests`, `IrVersionTests`.
