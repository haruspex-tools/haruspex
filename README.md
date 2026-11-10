# haruspex

A static analyser for SQL Server database projects. It reads the `.sql` files of a database project
and infers the shape of what each stored procedure returns, including `FOR JSON` payloads, which SQL
Server itself reports only as a single `nvarchar(max)` column. From that shape it generates
[Zod](https://zod.dev) schemas and TypeScript types, so an application can call its procedures with
types it can trust.

It never connects to a database. Everything is inferred from the source.

**The rule it follows: never claim more than the engine guarantees.** A field reported as non-null is
never null. When something cannot be proved, haruspex says it is unknown and why, and does not guess.

## Status

**Pre-release.** The command line, the intermediate representation (IR) and the generated code will all
change without notice. Do not depend on it yet.

What works today: `CREATE TABLE` and `CREATE PROCEDURE` in a folder of `.sql` files; procedures whose
result is a plain `SELECT` over tables, with inner and left joins; `FOR JSON PATH` with nested objects
from dotted aliases, `WITHOUT_ARRAY_WRAPPER`, `INCLUDE_NULL_VALUES`, and nested arrays from correlated
`FOR JSON` subqueries; the JSON constructors `json_object`, `json_array`, `json_arrayagg` and `json_objectagg`
(SQL Server 2022 for the first two, 2025 or Azure SQL for the aggregates), alone, nested, inside `FOR JSON` and in
correlated subqueries; the native `json` type; and scalar functions that return JSON, including functions that call
themselves to build trees, which become recursive types (Zod getters); and procedures built from several statements:
temporary tables (`CREATE TABLE #t`, `SELECT … INTO`, `INSERT`, `UPDATE`, `ALTER`, `DROP`), table variables, variables,
`IF`, `WHILE`, `TRY … CATCH`, `RETURN` and `THROW`, and CTEs, recursive ones included. Anything else is reported as
unknown, with a reason.

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

It also holds per-schema exclusions and name prefixes, overrides of what was inferred (each still checked at runtime), naming rules and strict
objects: see [docs/configuration.md](docs/configuration.md).

## Who it is not for

- **Anyone who needs a supported tool.** This is a personal open-source project with no support
  commitment.
- **Databases other than SQL Server.** The IR is dialect-neutral, but SQL Server is the only front end.
- **Code that builds its results at runtime.** The shape of dynamic SQL, `EXEC @procedure` or a CLR
  procedure is in no source file, so haruspex cannot type it, and it says so.
- **Anyone looking for an ORM.** Haruspex goes the other way: the database owns the interface, and the
  application's types are derived from it.

## Licence

[Apache-2.0](LICENSE). Contributions are welcome; see [CONTRIBUTING.md](CONTRIBUTING.md).
