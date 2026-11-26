# XML's `nodes()`, `query()`, `FOR XML` subqueries and `OPENXML` are typed as the server gives them

**Date:** 26 Nov 2026

**Decision.** Four XML expressions that [decision 0133](0133-an-xml-value-is-a-kind-of-its-own-a-branded-string.md) left unknown
are typed, each from what SQL Server was observed to give (`XmlExpressionObservationTests`). None is typed from a schema
collection: that is still the follow-up 0133 names.

- **`x.query('path')`** is untyped `xml`, NULL exactly when `x` is. Where the path finds nothing it gives an empty instance, not
  NULL. Its result is never typed by `x`'s collection.
- **`x.nodes('path') AS t(c)`** in `FROM` or `APPLY` is a source, the symbol `XmlNodesCall`, of any number of rows for each
  outer row: none for a NULL instance or a path that finds nothing. As for any source, an `OUTER APPLY` keeps the outer row
  with NULL. Its one column is the node, an `xml` value that is never NULL, which XML's methods read as any `xml` value. The server
  takes the column only as a method's target or in `IS NULL` (493, 525), so read anywhere else, a select list and `*` included,
  it is unknown with that reason: it is never returned to a client, so it has no type in a result.
- **A `FOR XML` subquery** is `xml` with `TYPE` and `nvarchar(max)` without, in every mode and with any other option. It is
  NULL exactly when its query returns no rows (`empty-subquery`), so it is never NULL when the query always returns one row.
  `STUFF((SELECT ',' + name … FOR XML PATH('')), 1, 1, '')` is then typed by `STUFF`'s own rule. Without `TYPE` the text escapes
  `&`, `<` and `>`, which is a fact about the value, not the type. A derived table or CTE whose query is `FOR XML` is exactly
  one row, over no rows too, holding that value: before this, its column was typed as its query's own column, never NULL and
  of any number of rows, which the server contradicts. A result set returned as `FOR XML` keeps 0133's note, HAR2026.
- **`OPENXML … WITH (…)`** gives its `WITH` columns, each of its declared type. Its value is the text the column's path finds,
  converted as `CAST` converts `nvarchar`, so its facets and its NULLs under the callers' options are a conversion's. It is
  always nullable, with the null rule `xml-value`, which 0133 made for `value()` and whose text now names `OPENXML` too: NULL where
  the path finds nothing, its `at` the `OPENXML` call. `WITH` a table named with its schema gives that table's columns, read
  the same way, so NULL even where the table declares `NOT NULL`. Only the types observed are read: strings, numbers, `bit`,
  dates and times, `uniqueidentifier`, binary and untyped `xml`. Without `WITH` (the edge table) it stays unknown. `sp_xml_preparedocument` and `sp_xml_removedocument` join the catalogue of system procedures:
  each returns no result set.

**Context.** After 0133 the harvest's largest XML gap was the 62 `value()` calls in ProfilesRNS on `nodes()` rows, all unknown by
the source's reason, and then 20 `FOR XML` subqueries, most of them the string-join idiom. The maintainer asked to go further
with XML expressions and to include `OPENXML`, and each of these has one result type whatever the document holds.

**Alternatives.**
- *Type the node column as `xml` wherever it is read.* The server refuses it outside a method or `IS NULL`, so a type there
  would describe a value no client receives.
- *Leave `query()` for the collections.* Its result is untyped `xml` whatever the collection, so waiting would gain nothing.
- *A null rule of its own for `OPENXML`.* It would mean the same as `xml-value`, NULL where a path finds no node, and would
  change the IR. Provisional until the maintainer settles it.
- *Type a `FOR XML` subquery whose columns are unknown.* Its type doesn't depend on them, but such a column may be one the
  server refuses there, which would make the query an error, so it stays unknown.

**Reason.** Each type is the one the server describes and returns for every document, and each NULL is the one observed, so
nothing is claimed that a document could break.
