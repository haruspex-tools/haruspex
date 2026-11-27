# FOR JSON AUTO is typed from observed nesting

**Date:** 27 Nov 2026

**Decision.** `FOR JSON AUTO` is typed by rules observed on the server, each one a test, and `ROOT` as the object around the
array it wraps. Under `AUTO` a source of the query's `FROM` is a table or not: a table, a view, a derived table, `VALUES`, a CTE,
a function of the project, a temporary table, a table variable, `OPENXML` and `nodes()` are; `OPENJSON`, `STRING_SPLIT` and
`GENERATE_SERIES` are not. A table whose bare column the select list holds gets an object of its own, and the objects nest in
a chain, each inside the one before it, in the order their tables' first columns stand. A table's columns all go in its object,
wherever they stand. Any other column (an expression, a function or conversion of a column, a column under `COLLATE`, an
enclosing query's column, or a column of a source that is no table) goes in the deepest object opened before it. Each inner
object is in an array under its table's alias, or the name the `FROM` writes, as it writes it, and the array is the last key of
the object around it.

An inner array has at least one item, and exactly one when the query returns at most one row: each object comes from a row,
and each row writes an object at every level, one of no keys for an outer join's unmatched side. Nothing more is claimed of how
many items there are, since rows merge into one object only while they are adjacent. A key is left out when null by the rule
`PATH` follows; an object is never left out. `ROOT('name')` puts the array under that name, in either mode, and its array has a
row's item at least, since over no rows there is no document. `ROOT` without a name is `root` under `PATH`, but under `AUTO` a
source's name (the `FROM`'s first, even one that gives no column, or an `APPLY`'s), so it stays unknown. The IR needs nothing new: these are the objects, arrays and bounds `PATH`
already gives, so its version stays.

What was not observed stays unknown (`unsupported-query`), saying which form: `AUTO` over a set operation, a pseudo-column, a
column binding can't resolve, a full-text function's or `inserted`'s column, a function call binding can't resolve, a name
with an empty part, a synonym without an alias (keyed by its target as `CREATE SYNONYM` wrote it), and `ROOT` without a name
under `AUTO`. A query whose `FROM`
has no table is refused by the server (13600), and two keys of one name, which `AUTO` writes where `PATH` refuses them, are
`duplicate-column`.

**Context.** `AUTO` and `ROOT` were `unsupported-query`, and the first public corpus's JSON search procedures use both.
Microsoft's page describes the nesting in a few sentences, by the tables of the select list and their order, which leaves open
what a table is, where a column of no table goes, which key an array takes, and what an outer join writes. Observation settled each, and several
differ from what the page suggests or from `PATH`: `OPENXML` is a table and `OPENJSON` is not; `upper(l.sku)` opens no object
where `(l.sku)` does; a key written twice is written twice; a parent whose rows are apart is written twice; an unmatched row is
an object of no keys, never an empty array.

**Alternatives.**
- *Nest by the joins.* A table joined to the first could nest under it, as the `FROM` reads. Observed otherwise: the select
  list's order alone decides, so a table joined to the outermost nests under whichever table was selected before it.
- *Claim the parents unique, one object per key.* That holds only when the query orders by the parent; a parent whose rows are
  apart is written twice, and an order is not proof, so the shape claims every order.
- *Model the sources not observed by their likeness* (a full-text function as a function). Every likeness checked so far had
  exceptions (`OPENXML` against `OPENJSON`), so a form not observed is unknown until it is.
- *Accept a repeated key and take the last value*, as JSON parsers do. Which value a parser keeps is the parser's choice, not
  the server's.

**Reason.** `AUTO`'s rules aren't documented closely enough to type from the page, and a wrong rule would put a key in the wrong
object, which a schema would then reject in every response. Each rule rests on an observation test, so a change in a SQL Server
version shows as a red test rather than a wrong type.

**What it costs.** The array's items aren't counted beyond one, so a client can't rely on one object per parent. A query over a
source not observed, or over a set operation, stays unknown though the server would answer it.

**In the code:** `Typer.ForJsonAuto.cs` (the nesting), `Typer.ForJson` (the array, `WITHOUT_ARRAY_WRAPPER` and `ROOT`),
`BoundSource.WrittenName` (the key), `RowColumn.Bare`; `ForJsonAutoTests`, `ForJsonObservationTests.Auto.cs`, the `nests` fixture;
the rules in [the inference reference](../../src/Haruspex.SqlServer.Inference/README.md#for-json-auto-and-root).
