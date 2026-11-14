# haruspex

A static analyser for SQL Server database projects. It reads the `.sql` files of a database project, or
the DACPAC it builds, and infers the shape of what each stored procedure returns, including `FOR JSON` payloads, which SQL
Server itself reports only as a single `nvarchar(max)` column. From that shape it generates
[Zod](https://zod.dev) schemas and TypeScript types, so an application can call its procedures with
types it can trust.

It never connects to a database. Everything is inferred from the source.

**The rule it follows: never claim more than the engine guarantees.** A field reported as non-null is
never null. When something cannot be proved, haruspex says it is unknown and why, and does not guess.

## Status

**Pre-release.** The command line, the intermediate representation (IR) and the generated code will all
change without notice. Do not depend on it yet.

What works today, each with an invented database in [`tests/fixtures`](tests/fixtures) that the differential tests run
on a real SQL Server:

- **The schema**, from a folder of `.sql` files or a DACPAC with the DACPACs of the same database it refers to (read from
  its `model.xml`, without DacFx): tables with their `CHECK` constraints, keys and unique indexes, views,
  functions, procedures, DML triggers, synonyms and alias types. What the schema proves reaches the generated code: a
  column's `NOT NULL`, a `CHECK`'s set of values or range, a string's length.
- **Queries**: `SELECT` with inner, left, `FULL OUTER` and `CROSS` joins, `CROSS APPLY` and `OUTER APPLY`, derived
  tables, CTEs (recursive ones included), `VALUES` lists, views, `UNION`, `UNION ALL`, `INTERSECT` and `EXCEPT`, `TOP`,
  subqueries, `GROUP BY` with `ROLLUP` and `GROUPING SETS`, and system-versioned tables read `FOR SYSTEM_TIME`. Filters,
  keys and single-row queries refine what is proved: `WHERE c IS NOT NULL` makes `c` never null, and a lookup by key
  returns at most one row.
- **Expressions**, each typed as SQL Server types it: literals, `CAST` and `CONVERT` and their `TRY_` forms, arithmetic
  (decimal arithmetic to 38 digits included), `COALESCE`, `ISNULL`, `CASE`, `IIF`, `CHOOSE` and `NULLIF`, aggregates (`AVG`
  and the statistical ones included), window functions, and the common date, string and numeric functions, `JSON_VALUE`,
  `STRING_AGG` and `GENERATE_SERIES`.
- **JSON**: `FOR JSON PATH` with nested objects from dotted aliases, `WITHOUT_ARRAY_WRAPPER`, `INCLUDE_NULL_VALUES` and
  nested arrays from correlated subqueries; the constructors `json_object`, `json_array`, `json_arrayagg` and
  `json_objectagg` (SQL Server 2022 for the first two, 2025 or Azure SQL for the aggregates); the native `json` type;
  `OPENJSON`, with and without `WITH`; scalar functions that return JSON, including ones that call themselves to build
  trees, which become recursive types (Zod getters); and JSON built in the project, traced through variables, tables,
  functions and JSON literals to where it is read.
- **Functions**: scalar functions, and inline and multi-statement table-valued functions read in `FROM`, a join or
  `APPLY`.
- **Procedures of several statements**: temporary tables (`CREATE TABLE #t`, `SELECT … INTO`, `INSERT`, `UPDATE`,
  `DELETE`, `ALTER`, `DROP`), table variables, variables, `IF`, `WHILE`, `TRY … CATCH`, `RETURN` and `THROW`; `EXEC` of
  the project's procedures and `INSERT … EXEC`, recursion included; writes to permanent tables (`INSERT`, `UPDATE`,
  `DELETE`, `MERGE`, `TRUNCATE`) with `OUTPUT`, and the triggers they fire; and cursors.
- **Declared results**: where a procedure's results can't be inferred (dynamic SQL, say), the config file can declare them
  as a JSON Schema; they stay checked at runtime, and flow into the results of the procedures that call it.
- **Read uncommitted**: a `NOLOCK` hint, or `READ UNCOMMITTED` set in the project, withdraws the claim that a lookup by
  key returns at most one row.

How each is read, and the observations each rule rests on, are in
[the inference README](src/Haruspex.SqlServer.Inference/README.md). Anything else is reported as unknown, with a reason.
How haruspex works, and why a static analyser is built this way, is explained in [the guide](docs/internals/README.md), and
the decisions it rests on, with the alternatives, in [the decision log](docs/decisions/README.md). How much of a real
database it types is measured on two of Microsoft's sample databases, by a test that writes
[the corpus figures](docs/corpus-figures.md).

```sh
haruspex generate path/to/database --include 'sales.*' --out src/db
```

This writes `haruspex.ir.json` (the inferred shapes) and `haruspex.zod.ts` (a Zod schema and a type per
procedure) to `src/db`. A project keeps its settings in a `haruspex.json` beside it, found by walking up from the
working directory, so `haruspex generate` alone is enough:

```jsonc
{
  "$schema": "https://github.com/sharpblue/haruspex/raw/main/schemas/haruspex.schema.json",
  "source": "database",
  "out": "src/db",
  "schemas": { "sales": { "include": ["get_*", "list_*"] } }
}
```

It also holds per-schema exclusions and name prefixes, overrides of what was inferred (each still checked at runtime),
results declared where they can't be inferred, naming rules and strict objects: see
[docs/configuration.md](docs/configuration.md).

## Who it is not for

- **Anyone who needs a supported tool.** This is a personal open-source project with no support
  commitment.
- **Databases other than SQL Server.** The IR is dialect-neutral, but SQL Server is the only front end.
- **Code that builds its results at runtime.** The shape of dynamic SQL, `EXEC @procedure` or a CLR
  procedure is in no source file, so haruspex cannot type it, and it says so.
- **Anyone looking for an ORM.** Here the database owns the interface, and the application's types are derived
  from it.
- **Anyone who won't use code written by AI.** The project is built by agentic engineering. AI coding
  agents write and review each change, in small slices, and every rule is checked by observing a real SQL
  Server. A person decides what is built and merges every change. If that rules it out for you, it's
  better to know before you start.

## Licence

[Apache-2.0](LICENSE). Contributions are welcome; see [CONTRIBUTING.md](CONTRIBUTING.md).
