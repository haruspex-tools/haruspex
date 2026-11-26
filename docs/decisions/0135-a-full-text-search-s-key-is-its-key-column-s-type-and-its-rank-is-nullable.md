# A full-text search's key is its key column's type, and its rank is nullable

**Date:** 26 Nov 2026

**Decision.** `CONTAINSTABLE` and `FREETEXTTABLE` in `FROM` are relations of two columns, `KEY` and `RANK`. `KEY` is of the type
of the table's full-text key column, and never null: the project's `CREATE FULLTEXT INDEX … KEY INDEX ix` (in a folder) or the
`SqlFullTextIndex` element's `KeyName` (in a package) names a unique index, unique constraint or primary key of the table, which is
followed to its one column. Only the type's own facts are claimed, in the column's collation; the column's CHECK constraints and
value sets are not. Where the project has no full-text index on the table, creates it more than once with different keys, or
names a key it doesn't define, `KEY` alone is unknown with the reason (`unsupported-source`), and `RANK` is still typed. `RANK`
is an `int`, which may be null (`nullable-column`, at `CONTAINSTABLE.RANK` or `FREETEXTTABLE.RANK`). The semantic search
functions (`SEMANTICKEYPHRASETABLE` and the others) are unknown with a reason of their own. The IR's format doesn't change.

What the server does, observed in `FullTextObservationTests` on SQL Server 2025 with full-text search
(`tests/Haruspex.DifferentialTests/Server/FullText`):

- **Two columns, whatever the call.** A column list, `*`, a language and a count of rows each leave `KEY` and `RANK`.
- **`KEY` is the key column's type and collation, never null.** An `int` key gives `int`, an `nvarchar(20)` of
  `Latin1_General_100_CI_AS_SC_UTF8` gives that, and a column of an alias type gives its base type, each described as not
  nullable. The key index must be unique, of one column, never null, without a filter and unmasked (7653, 16014), a mask can't be
  added to the column after (16013), and the index can't be dropped while the full-text index stands (7613).
- **`KEY` is read from the full-text index, not the table.** A row deleted in a transaction not yet committed still gives its key,
  so `KEY` doesn't say its row is there, and a CHECK of the column is not claimed of it.
- **`RANK` is described as nullable**, by `sys.dm_exec_describe_first_result_set` and by the column a `SELECT INTO` makes of it;
  no search seen, of any form tried, gave a NULL, and every value seen was from 0 to 180.
- **Without an alias the call is known by its table's name** (`note.[KEY]`), not by its full name, and a table of the same name
  beside it is an error (1012).

**Alternatives.**

- **`RANK` never null**, as Microsoft documents it and as every observation shows. Lost, provisionally: the server declares the
  column nullable, and haruspex claims what the engine guarantees, not what it has been seen to do. An observation can show a
  NULL, but not that none can come. The cost is a `.nullable()` on a value a caller can test once; claiming the opposite wrongly
  costs a runtime failure. If the maintainer prefers the observed rule, it is one line in `Typer.FullText.cs` and this record.
- **`RANK` from 0 to 1000**, as documented. Lost: no observation reaches the bound, so the range is documentation alone.
- **`KEY` with the column's CHECK facts and description, traced to the column.** Lost: a key may be of a row deleted, or written
  and rolled back, which the column no longer holds.
- **Following a `DROP FULLTEXT INDEX`.** Lost as unneeded: a table has one full-text index at most, so a source that drops one and
  creates another creates it twice, and both keys are kept; if they differ, `KEY` is unknown. One dropped and not created again
  makes the search an error (7601), not a value.

**Why.** Real projects search text this way, and until now every column read through it was unknown. The key column is in the project in every case that deploys, so `KEY` can be typed exactly, and `RANK`'s type is fixed.
