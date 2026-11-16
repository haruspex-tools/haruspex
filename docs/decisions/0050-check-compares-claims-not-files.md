# `check` compares claims, not files, and fails when there is nothing to compare

**Date:** 16 Nov 2026

**Decision.** `haruspex check` infers as `generate` does, writes nothing, and compares what it would write with what is
committed in the output folder, in two ways:

- **The IR, claim by claim** (`IrComparison`): objects by id, parameters and fields by name, result sets by position, each
  type followed through its references. A claim is what a reader of the IR or of the generated code may rely on. What only
  says where a claim came from is not compared: locations, the reasons a value may be null (only whether there is one), the
  sources of facts, the package read and its hash, why an unknown is unknown, the overrides (their effect is in the types),
  and a type's identity.
- **The module, as text**, with line endings set aside, since git may convert them on checkout and they mean nothing to the
  module.

It fails, with exit code 1, when a claim differs, each change one error naming the object, the path and how it changed
(`shop.list_orders results[0][].note: became nullable`), at the procedure's place in the SQL; when the module differs though
no claim does, which a module written by another version of haruspex, or edited by hand, gives; when a selected procedure's
result sets can't be listed and the config file doesn't declare them; and, when the config file asks for it
(`"check": { "unknowns": "fail" }`), when the module has an unknown. An unknown is otherwise a note, as in `generate`.

**When there is nothing to compare with, it fails too**, with exit code 1 and a code of its own:
- **A committed file is missing** (HAR6011): the IR, the module or both. The message names each and says to run `generate`
  and commit what it writes.
- **The committed IR is of another version** (HAR6012): the reader refuses it ([decision 0024](0024-an-ir-reader-refuses-other-versions.md)),
  so no claim can be compared. The message names both versions and says to run `generate` with this version and read the
  diff of the module before committing it.
- **The committed IR can't be read** (HAR6013): not JSON, or not an IR document, with where the reader stopped.

**Context.** `check` exists for CI: a database change that would change what an application compiles against should fail
the build before it is deployed, and say what changed in terms a developer can act on. The committed IR and module are the
contract between the database and its consumers (consumer-driven contract testing, as Pact does for HTTP services).

**Alternatives.**
- *Compare both files byte for byte.* Simple, and exact about the files, but the IR records where each claim came from: a
  comment added above a procedure moves every location after it, and a DACPAC's hash changes on every build, since a build
  records when it ran. Such a check fails on every change to the database, however harmless, and a check that always fails is
  ignored. It also says only that a file differs, not what changed.
- *Compare only the module.* What the application compiles against is the module, but a text diff of generated code is
  not a summary, and the IR claims what the module doesn't yet say: parameters, how many rows a result has.
- *Compare only the IR.* It would pass a module that no longer matches it: one edited by hand, or written by an older
  emitter.
- *Pass, or warn, when nothing is committed.* A new module that was never committed, or an `out` that names the wrong folder,
  would then pass CI, and the check would protect nothing without saying so.
- *Read an IR of another version anyway, or compare only the modules then.* The reader refuses other versions because a
  claim's meaning may have changed between them (0024); comparing the modules alone would pass such a change unseen. A new
  version of haruspex is a change to review, like a change to the database.
- *Exit code 2 when nothing is committed.* Code 2 says the command line or the config file is wrong; here they may be right,
  and the contract isn't committed yet. The check fails, which is 1.

**Consequences.** The committed IR may lag behind in what isn't compared (a location, a hash) without failing the check;
running `generate` brings it up to date, and the diff then shows only those lines. A change that only narrows what a reader
receives (a field that becomes non-null) fails the check as a widening does: the module changes either way, and the developer
decides whether to commit it.

**In the code:** `IrComparison`, `IrChange`, `Check` in the CLI; the CLI's README; [HAR6011](../diagnostics/HAR6011.md) to
[HAR6017](../diagnostics/HAR6017.md).
