# Collections that compare by value are a layer of their own

**Date:** 13 Nov 2026

**Decision.** The collections that compare by value (`EquatableArray`, and any other a record needs) live in one project,
`Haruspex.Collections`, at the bottom of the layers beside `Haruspex.Diagnostics`: it refers to nothing of haruspex's, and
every layer that holds a list or a map in a record refers to it. It is packable, as Diagnostics is, because
`Haruspex.SqlServer.Model` holds its types in its public API, so a package of the model depends on a package of these.

**Context.** Every record of haruspex compares by value ([decision 0022](0022-values-compare-by-structure.md)), so every list
or map a record holds must be one that compares by value. `EquatableArray` was in `Haruspex.Ir`, which the IR's records
needed first. The model, the syntax reader, binding and the sources may not refer to the IR (the table in
[docs/architecture.md](../architecture.md)): the IR is the output of inference, and a linter that reads a database project
has no use for it. So their records held .NET's collections, which compare by reference.

**Alternatives.**
- *A copy in each layer that needs one.* Two copies of one equality drift apart, which is the first finding of the
  architecture review this structure answers: one concept, several copies.
- *In `Haruspex.Diagnostics`*, already at the bottom and already packable. A collection is not a diagnostic: the project's
  one reason to exist would become two, and its name would lie about one of them.
- *In `Haruspex.SqlServer.Model`*, which the SQL Server layers already refer to. The IR and the emitters may not refer to
  the model, and the IR holds most of the lists.
- *Records that keep .NET's collections, with equality written by hand.* Each hand-written equality is a place a field added
  later is forgotten, which is what generated equality is for.

**Reason.** One definition of "two lists are equal", usable by every layer without teaching any layer what another knows.
The cost is one more small package for anyone publishing the model, as Diagnostics already is.

**See also:** `src/Haruspex.Collections/README.md`.
