# Names that differ only in case are unknown

**Date:** 22 Nov 2026

**Superseded in part** by [decision 0119](0119-names-that-differ-only-in-case-are-an-error.md) (24 Nov 2026): a pair in a table, a
table type, a schema's objects, or a body's variables, temporary tables and declared tables is now an error found where each is
read. What this record decides still holds for a pair reading doesn't see (the columns of a derived table, a CTE, a view or a
temporary table `SELECT … INTO` makes, and a column `ALTER TABLE #t ADD` adds beside its case variant) and for a model built
otherwise. A callee's temporary table whose name differs only in case from its caller's makes the call unknown (decision 0119).

**Decision.** Names are matched ignoring case: a column, a variable and a temporary table are found by their name in any
case. Where two columns of one table, or two variables or table variables of one body, have names that differ only in case
(`[Code]` and `[code]`, `@Total` and `@total`), a reference to either is unknown, with a reason naming both. A `SELECT *` gives each
of them as unknown, and the table's other columns as before. This covers a table of the schema, a table type, a body's
temporary tables and table variables, and the `inserted` and `deleted` of a write. A CHECK constraint proves nothing of
either column (a table type's row included), since its names too are matched ignoring case.

**Context.** Matched ignoring case, one of two such columns is read for the other: on a database of
`Latin1_General_100_CS_AS`, with `Code varchar(10) NOT NULL`, `code int NULL`, `qty int NULL CHECK (qty BETWEEN 1 AND 5)` and
`Qty int NOT NULL`, `SELECT code, Qty` was typed as a string of at most 10 characters and an integer from 1 to 5 that may be
null. The server returns an integer that may be null and an integer that is never null. Such names can be made only where names
are compared by case: a case-insensitive database refuses the second column (Msg 2705), and a case-insensitive server the second
variable (Msg 134). So their presence is proof that the matching is wrong there, and no other proof is at hand.

**Alternatives.**
- *Match names by case where the database's collation is case-sensitive.* That is the complete answer, and a design of its own:
  object names follow the database's collation, variables and temporary tables the server's, which the source doesn't record,
  and every one of the comparisons in the model and the binder would take the collation. It is left for the maintainer to
  decide, and this decision stands until then.
- *Refuse the project with an error.* Sound, but it would stop every procedure of a database for one table no procedure reads.
- *Keep the first column.* That is the bug.

**Reason.** With no such pair, matching without case gives what matching by case gives, for any script that deploys. With one,
an unknown with a reason claims nothing, and the rest of the table and the body are typed as before. The check is made where a
name is looked up in a body (`Binder.Resolve`, `Binder.Star`, `BodyDeclarations`), and where a CHECK is read
(`CheckReader.Read`), whose facts reach a table type's row without a lookup. The model's other lookups by column name
(`ITable.Column` in a view's columns (`Typer.Views.cs`) and `Cardinality`'s keys, `DeclaredColumns`, `ColumnDeclarationCheck`,
`KnownTable.IndexOf`) still take the first of a pair. None was found to give a wrong type, since every read of such a column
is unknown, but they are not guarded.

**In the code:** `Binder.CaseVariants`, `CaseVariantVariable`, `CheckReader.Read`; tests in `BinderTests.CaseVariants.cs`,
`ConstructTests.CaseVariants.cs`, `ParameterTests.CaseVariants.cs` and `SchemaFactsTests.CheckConstraints.cs`.
