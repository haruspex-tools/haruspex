# Fixtures

A fixture is a database that the tests read, run and hold haruspex to. Each is one folder here, holding everything about
it: what haruspex reads, what it must produce, and how the differential tests deploy and call it. Fixtures are invented, and
no part of one comes from a real system, with one exception: two fixtures, `wide-world-importers` and `adventure-works`,
take their tables from Microsoft's sample databases, vendored in [`tests/corpus`](../corpus/README.md) under the MIT licence,
whose notice each keeps beside it. They hold the examples in [the project's README](../../README.md) to a real server.

The tests find the fixtures by listing this folder (`Fixture.All`, in `tests/Shared`), never from a list of names, so
the snapshot, emit-check, seed, coverage and acceptance tests can't disagree about which fixtures exist. Why it is built
this way is in [the guide's chapter *Observing the engine*](../../docs/internals/09-observing-the-engine.md).

## What a fixture's folder holds

| Path | What it is | Who reads it |
|---|---|---|
| `README.md` | What the fixture is for: the constructs it shows, and the cases its seed reaches. | you |
| `database/` | The schema: `tables/`, `views/`, `functions/`, `procedures/` and `triggers/`, one object to a `.sql` file. It is what haruspex reads, and nothing else is in it. | `haruspex generate`, and the differential tests, which deploy it |
| `haruspex.json` | The config file, when the fixture has one: declared results that inference can't see (`calls`), JSON a column holds (`parcels`), or, for a project, the whole command's settings (`shop`, `market`). | `haruspex generate` |
| `expected/` | What haruspex must produce: the reviewed snapshots. | the snapshot tests |
| `seed.sql` | The rows inserted after the schema is deployed, chosen to reach every case the IR allows. | the differential tests |
| `runs.json` | The calls the differential tests make, with their arguments. | the differential tests |
| `schemas/` | JSON Schemas the config file names for JSON a column holds (`parcels`: `"json": "schemas/options.schema.json"`). | `haruspex generate`, through the config file |
| `queries/` | A project's query files, in the folder its config file's `"queries"` names (`shop`, `market`): typed and emitted as the command does, so they are checked by the project's snapshots. | `haruspex generate` |
| `sql/` | Query files, when the fixture has any: the application's own SQL, typed against `database/` as `"queries": "sql"` in a config file in the fixture's folder would type it, so each query's id is its path from there (`sql/getOrderLines.sql`). Only a deployed fixture may have them, and each must be run. | the differential tests, which call each through its generated function |

The schema has a folder of its own so that haruspex reads exactly the database: the seed is SQL too, and haruspex reads
every `.sql` file under the folder it is given. So the command line names `database/`:

```sh
dotnet run --project src/Haruspex.Cli -- generate tests/fixtures/orders/database --include 'sales.*' --out out
```

## Two kinds of fixture

- **A fixture checked per procedure** (every one that isn't a project). Its `expected/` folder holds, for each procedure,
  its IR (`sales.get_order.ir.verified.json`), its Zod (`sales.get_order.zod.verified.ts`) and its JSON Schema
  (`sales.get_order.jsonschema.verified.json`), named for the procedure and what they hold; `FixtureSnapshotTests` fails on a
  procedure without all three, a snapshot without a procedure or any other file there. It has a seed and
  runs, and the differential tests deploy it, call every procedure and check every value against the IR.
- **A project for the CLI**: a fixture whose `haruspex.json` names a `source` folder (`Fixture.IsProject`; today `shop`,
  `market`, `glossary` and `tariffs`). Its config chooses the procedures and names them, so what it must produce is the command's files, each as a
  snapshot named for it: the IR (`haruspex.ir.verified.json`), one file for each emitter its config names
  (`haruspex.zod.verified.ts` and the rest) and the list of what the command wrote (`haruspex.files.verified.json`). A config
  that splits the output (`market`, `"split": "schema"`) writes a file for each module and emitter instead
  (`sales.zod.verified.ts`, `sales.mssql.verified.ts`), and what two modules share in `shared.zod.verified.ts`.
  `FixtureSnapshotTests` checks them by running the CLI in the fixture's folder, as a user would; anything else in
  `expected/` fails it. It is not deployed, so it has no seed or runs; its
  README says why.

A folder that lacks a part its kind needs fails discovery, and with it every test that uses a fixture: a fixture can't
be half added and quietly skipped.

## `runs.json`

JSON, with comments and trailing commas allowed (as in `haruspex.json`), so each choice can say why it is there.

```jsonc
{
  // Why the seed is held to every edge, the types' too, or isn't.
  "seedReachesEveryEdge": true,

  // An edge no run can reach, with the reason.
  "edgesNotReached": {
    "sales.list_orders results[0][].status": "It lists open orders only, so that is all status can be."
  },

  // SQL run before a call, in a transaction that is rolled back afterwards, by name.
  "setups": {
    "no orders": "DELETE FROM sales.order_line; DELETE FROM sales.[order];"
  },

  "runs": [
    { "name": "get_order, no such order", "procedure": "sales.get_order", "parameters": { "@order_id": 999 } },
    { "name": "list_orders, no orders", "procedure": "sales.list_orders", "setup": "no orders" }
  ],

  // A procedure left out, with the reason. Every other procedure must be run.
  "notRun": {
    "sales.order_report": "It reads a table the fixture leaves out on purpose, so SQL Server cannot run it."
  }
}
```

| Key | Meaning |
|---|---|
| `seedReachesEveryEdge` | When `true`, the seed must reach every edge the IR claims, the types' too: each bound, each full length, each value of a set (`The_seed_reaches_every_edge_the_ir_claims`). Absent means `false`: the seed must still reach every edge of a fact the types alone don't give, from a CHECK, an expression's literals, the engine or a declaration, and each value of each set (`The_seed_reaches_every_edge_its_types_alone_do_not_give`). |
| `edgesNotReached` | Edges the seed is excused from reaching, each with the reason, such as a CHECK's set wider than the one value a procedure writes: a procedure (`depot.place_order`), a path in one (`depot.place_order results[0][].status`) or one edge (`… status: value picked`), as the test names them. An excuse that names no missing edge fails. |
| `setups` | Named SQL to run before a call. A run with a setup writes, so it runs alone, in a session that is rolled back. `writes` (`SET NOCOUNT ON;`) changes nothing: it is how a run of a procedure that writes asks for that session. |
| `runs` | Each call: a `name` unique in the file (the test shows it after the fixture's name, as in `orders: get_order, no such order`), the `procedure`, its `parameters` and a `setup`. Or a call of a query file: its `name`, the `query` by its path, and its `parameters`, with no setup. |
| `notRun` | Procedures deliberately not run, each with the reason (`Every_procedure_is_run_or_excluded_with_a_reason`). |

**A parameter's type comes from the procedure.** The file gives each argument as JSON: a number, a string, `true` or
`false`, `null`, or a date and time in ISO 8601 (`"2026-11-03"`, `"2026-11-03T08:30:00.125"`, `"08:30:00"`, or with an offset,
`"…+05:30"`), a GUID as a string, and a binary value as SQL writes one, `"0x"` and two hex digits a byte. The reader converts it to what the procedure declares (`tinyint`, `bigint`,
`date` and the rest), as an `EXEC` would, and the call sends it as that type, with its length, precision or scale, without the
file repeating it: `"0001-01-01"` reaches a `date` and `.1234567` a `datetime2(7)` unchanged (`RunArgument`). A key, setup,
procedure or parameter the reader doesn't know fails every run, rather than being ignored.

**A query file's arguments are as its function takes them.** A query's parameter types are inferred, not declared in the
database, so the reader has nothing to convert to: each value is given in the form the function's parameters' schema takes
(a date as its ISO 8601 string), and passed to the generated function as it is (`GeneratedFunctionTests`). A run of a query file
calls only that function, which checks what comes back with the query's schema; the procedures' check of every value against
the IR, and of the seed's coverage, doesn't reach query files yet.

## Adding a fixture

1. Make the folder, with `README.md` and the schema under `database/`. The schemas of all deployed fixtures share one
   database, so pick schema names no other fixture uses.
2. Write `seed.sql`, and the runs in `runs.json`, until the differential tests' coverage passes
   ([their README](../Haruspex.DifferentialTests/README.md#the-seed-must-reach-every-case)).
3. Run the tests: the snapshot tests write each procedure's `*.received.*` files into `expected/`. Check each against what
   SQL Server really does before accepting it as `*.verified.*` ([the snapshot helper](../Haruspex.Tests/Snapshotting/README.md)).

Nothing else names the fixture: the emit check compiles its Zod with `tsc`, and the differential tests deploy, seed and
call it, because they find it.
