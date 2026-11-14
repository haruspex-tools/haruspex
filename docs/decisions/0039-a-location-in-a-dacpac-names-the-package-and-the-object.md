# A location in a DACPAC names the package and the object

**Date:** 14 Nov 2026

**Decision.** A location in something read from a DACPAC is written as a file, a line and a column, as every location is, with
the package and the object as the file: `sales.dacpac/[sales].[get_order]:3:5`. The object's name is always bracketed, with a
`]` in it doubled, so a name holding `:`, `/` or `.` can't be misread. In a module (a procedure, function, view or trigger) the
line and column are in the module's own text, whose first line is 1. In a table, which `model.xml` holds as elements with no
text, the line is the part's place in the table's list (its columns in order, then its CHECK, key, foreign key and index
elements in the order `model.xml` gives them) and the column is 1.

**Context.** A DACPAC keeps no file names: a project's scripts become elements of `model.xml`, and a module keeps its text (a
header of `CREATE … AS` and a body) with the line and column it started at in a file the package doesn't name. A package
extracted from a database by SqlPackage doesn't even keep that: every module starts at line 1, since the server keeps only
the module's text (observed in `tests/dacpacs`). Every location the IR and the diagnostics carry is `file:line:column`
(`SourceLocation`), read and written as such by tools and editors.

**Alternatives.**
- *The project file's line*, from the module's recorded start. It would match a folder's locations line for line, but it
  holds only for a built package, and even then names no file. In an extract the same procedure would be at another line, so
  a location would mean two things.
- *A location type of its own for a package*, with no line. Every layer that carries a location would have to know two
  kinds, and the model would learn where its schema came from, which it must not
  ([the layer table](../architecture.md)).
- *The object without its package.* With references, one database is read from many packages, and which package an object
  came from is what a reader needs in order to find it.

**Reason.** The module's own text is what every package has, built or extracted, and it is what `sys.sql_modules` shows for
the object in a deployed database, so a location reads the same however the package was made, and can be found there. The
package and the object together name a place a person can open. For a table no text exists, so a line counts its parts;
that keeps two unnamed constraints apart, and says plainly it is a position in a list, not in a file. The proof that a DACPAC
reads as its project's folder rewrites each of the folder's locations by these rules, built from names, not from the
reader's own positions (`DacpacEquivalenceTests`).

**See also:** [The inputs and the model](../internals/02-the-inputs-and-the-model.md), "A DACPAC becomes the same model";
`PackageLocation`.
