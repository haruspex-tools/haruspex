# Fixture: glossary

An invented glossary whose entries, notes and labels are found by full-text search: `CONTAINSTABLE` and `FREETEXTTABLE` as
sources, with `KEY` of the type of each table's full-text key column and `RANK` an `int` the server declares nullable. Each
full-text index names a key of another kind: a primary key (`lex.entry`), a unique index created apart on a string of a
collation of its own (`lex.note`), a unique constraint on a column of an alias type (`lex.label`), and the primary key of a
table of the same name in another schema, a `char(8)` in a UTF-8 collation that isn't the database's (`archive.entry`).

It is a project for the CLI, not deployed with the fixtures the differential tests share: their server has no full-text
search, and a procedure that searches a table without a full-text index can't be created (7601).
`FullTextObservationTests` deploys it on a server that has, in a database of its own, runs each procedure and checks what it
returns against its IR, and `DacpacReaderTests` reads its package, `tests/dacpacs/fixtures/glossary.dacpac`.

| Procedure | What it shows |
|---|---|
| `lex.search_entries` | `CONTAINSTABLE` with a column list, a language and a count of rows, joined to its table on `KEY`: `KEY` an `int` |
| `lex.search_notes` | `FREETEXTTABLE` over every column, without an alias, so known by its table's name: `KEY` an `nvarchar(20)` |
| `lex.entries_with_notes` | `FREETEXTTABLE` on a left join's optional side, where `KEY` and `RANK` may have no row |
| `lex.search_labels` | `CONTAINSTABLE` whose key is of an alias type: `KEY` a `char(8)` |
| `lex.search_archive` | `CONTAINSTABLE` of `archive.entry`, named with its schema from a procedure of `lex`, which has a table of that name: `KEY` a `char(8)` of UTF-8, 2 to 8 characters |
