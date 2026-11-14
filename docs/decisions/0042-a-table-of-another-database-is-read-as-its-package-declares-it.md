# A table of another database is read as its package declares it

**Date:** 14 Nov 2026

**Decision.** A three-part name in a body (`[$(Ledger)].ledger.entry`) reads a table of another database when the DACPAC refers
to that database by the very name the body writes: a SQLCMD variable only through a reference that names it, never as a
database of that literal name, and a literal only through a reference whose literal it is. The table is read as its package
declares it, as this database sees it (`OtherDatabases.AsSeenHere`): named with its database as the body writes it, and so are
the CHECKs facts come from; its columns with their types and nullability, an alias type of that database given the built-in
type it stands for, and any other type of that database's own (a CLR type) named so that no type here can be taken for it, which
leaves the column unknown; each column with its effective collation, its own or its database's default, never this database's;
its CHECK constraints and unique keys as that package records them. A synonym is followed to its one target, here or there; a
synonym for a synonym is unknown, since SQL Server refuses to read through one (Msg 470, observed:
`Synonym_chaining_is_refused_when_read`). A project two databases both hold is read into each. A table of a database whose package records no collation isn't read. A view,
function or procedure of another database is unknown, with that reason: its body binds against its own database, which
inference doesn't do yet. The system's views (`sys`, `INFORMATION_SCHEMA`) are read by their two-part names from the `master`
package, when the source holds one; it lists their columns without types, so a column read from one is unknown for that reason.

**Context.** A database split across projects refers to its other parts as the same database, and to other databases by name or
SQLCMD variable; a procedure that reads another database's table was unknown, however well its package described the table.

**Alternatives.**
- *Keep another database unknown.* It would claim nothing false, and lose every column a package declares exactly.
- *Bind a variable by its value.* A SQLCMD variable's value is known only when the package is deployed; the package records it
  empty. Binding by the name the body writes is what the reference itself says.
- *Hand on the table as its own database holds it.* Every rule here resolves a type and a collation against this database: an
  alias type of that database would be missing, and a string column would take this database's collation, which can differ.

**Reason.** What a package declares of its tables is what that database enforces, when that package is what is deployed there.
That is the one new assumption, and it differs from one database split across projects: another database is deployed on its own
schedule. So the IR records each referenced package's SHA-256 (`source.references`), and a document is regenerated when another
database's package changes, as when this database's does.

**See also:** [The inputs and the model](../internals/02-the-inputs-and-the-model.md), "A DACPAC becomes the same model";
[decision 0040](0040-a-dacpac-is-read-with-the-packages-of-its-database.md).
