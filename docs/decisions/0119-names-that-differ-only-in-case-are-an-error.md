# Names that differ only in case are an error

**Date:** 24 Nov 2026

**Decision.** Two names in one namespace that differ only in case are an error (HAR1022), which stops the run. The namespaces
are a table's and a table type's columns, a schema's objects, and in each procedure, function, trigger and query file its
parameters and variables, its temporary tables, and the columns of each table it declares or creates. Two objects of one kind
remain HAR1002, whose message now names both spellings. Otherwise names keep being matched ignoring case. The maintainer chose
this on 24 Nov 2026, of the alternatives below.

**Context.** Every layer matches names ignoring case (`ObjectName`, `LocalName`, `ITable.Column`). That is an assumption, not
SQL Server's rule. A database's collation decides how its columns and objects are compared, and the server's how variables,
temporary tables and the tables a body declares are (observed on a database of `Latin1_General_100_CS_AS` on a server of
`SQL_Latin1_General_CP1_CI_AS`: the database takes two columns `[Code]` and `[code]` and a table `dbo.Thing` beside a view
`dbo.THING`, while the batch refuses `@Total` beside `@total` (134), `#t` beside `#T` (2714) and two such columns in a
temporary table or a table variable (2705)). With no such pair in a namespace, matching without case finds what matching by
case finds for any script that deploys, since a reference written in another case than its name is refused where names are
compared by case. With one, a reference to either was read as the first: `[code]` with `[Code]`'s `varchar(10)`, and `[Qty]` with
`[qty]`'s `CHECK` and nullability, where the server returns an `int` that may be null and one that is never null.
[Decision 0107](0107-names-that-differ-only-in-case-are-unknown.md) made such a reference unknown.

**Alternatives.**
- *A reference to either is unknown* (decision 0107). Sound, and it types the rest of the project, but it rests on finding the
  pair at each of the many places a name is matched, and the model's other lookups by column name were left unguarded.
- *Match names by case where the collation is case-sensitive.* Complete, but every comparison in the model and the binder would
  take a collation, a variable's being the server's, which no source records.

**Reason.** One check where each namespace is read guards every comparison after it, and a project that holds such a pair is
rare and fixed by a rename. The check runs once the model is read (`CaseVariants`), for a folder of scripts, a project and a
DACPAC alike, and when a query file is read. Decision 0107's unknowns stay, for the pairs reading doesn't see: the columns of a
derived table, a CTE, a view or a temporary table `SELECT … INTO` makes, which a database that compares names by case takes, and
a model built by other code.

**In the code:** `CaseVariants`, `SchemaDeclarations.Twice`; tests in `CaseVariantsTests`, `SqlProjectTests` and
`BindingObservationTests`.
