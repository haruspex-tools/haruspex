# A DACPAC is read with the packages of its database, found beside it first

**Date:** 14 Nov 2026

**Decision.** A DACPAC is read with every package of the same database it refers to, directly or through another, each once,
into one model. A package of another database, or `master.dacpac`, is found and its SHA-256 recorded, but not read: a name in
another database is not bound yet. A referenced package is looked for beside the package that refers to it, then in each
folder the config file's `references` names, then at the path recorded when the referring package was built; the first file
found is read. A reference found nowhere is one warning (HAR1003) naming every place looked; the run goes on, a name that binds
to nothing says it may be defined in the missing package, and the IR lists the reference with no hash. An object defined in
two packages of one database is an error, as two definitions in a folder are.

**Context.** One database is often split across projects, each building its own DACPAC and referring to the others it uses;
a real one has more than a hundred. A build copies every referenced package into the referring project's `bin` folder, and
records each reference with an absolute path on the build machine and, for another database, the database's name or the
SQLCMD variable that names it.

**Alternatives.**
- *Only the package given.* Every table of another project of the same database would be missing, and almost everything
  unknown, which is the case the references exist for.
- *The recorded path first.* It is right on the machine that built the package and almost never elsewhere; where it does
  exist, it may be an older build than the copy beside the package, and would win in silence.
- *A missing reference as an error.* It would stop the run for one missing package of a hundred, though everything that
  doesn't touch it can still be typed exactly; a warning, and unknowns that say where a name may be, claim nothing false.
- *A warning for each name it affects.* Which names a missing package would have defined can't be known; one warning names
  the cause, and the unknowns point to it.

**Reason.** The database is the sum of its packages, so the model is too. Looking beside the referring package first finds
what a build leaves where it leaves it, and the config's folders cover packages kept elsewhere, as a monorepo's build may.
The recorded path is the last resort, and the IR's hashes say which copy was read either way.

**Amended** (14 Nov 2026, [decision 0042](0042-a-table-of-another-database-is-read-as-its-package-declares-it.md)). A package
of another database, or `master.dacpac`, is now read too, with the packages of its own database, into a model of its own that
the model holds under the name a body writes the database by; its references to a third database are recorded, not read.

**See also:** [The inputs and the model](../internals/02-the-inputs-and-the-model.md), "A DACPAC becomes the same model"; `ReferenceFinder`,
`DacpacReader`; [HAR1003](../diagnostics/HAR1003.md).
