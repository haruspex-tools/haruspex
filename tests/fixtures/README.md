# Fixtures

A fixture is an invented database that the tests read, run and hold haruspex to. Each is one folder here, holding
everything about it: what haruspex reads, what it must produce, and how the differential tests deploy and call it. No
part of any fixture comes from a real system.

The tests find the fixtures by listing this folder (`Fixture.All`, in `tests/Shared`), never from a list of names, so
the snapshot, emit-check, seed, coverage and acceptance tests can't disagree about which fixtures exist. Why it is built
this way is in [the guide's chapter *Observing the engine*](../../docs/internals/09-observing-the-engine.md).

## What a fixture's folder holds

| Path | What it is | Who reads it |
|---|---|---|
| `README.md` | What the fixture is for: the constructs it shows, and the cases its seed reaches. | you |
| `database/` | The schema: `tables/`, `views/`, `functions/`, `procedures/` and `triggers/`, one object to a `.sql` file. It is what haruspex reads, and nothing else is in it. | `haruspex generate`, and the differential tests, which deploy it |
| `haruspex.json` | The config file, when the fixture has one: declared results that inference can't see (`calls`), or a whole project's settings (`shop`). | `haruspex generate` |
| `expected/` | What haruspex must produce: the reviewed snapshots. | the snapshot tests |
| `seed.sql` | The rows inserted after the schema is deployed, chosen to reach every case the IR allows. | the differential tests |
| `runs.json` | The calls the differential tests make, with their arguments. | the differential tests |

The schema has a folder of its own so that haruspex reads exactly the database: the seed is SQL too, and haruspex reads
every `.sql` file under the folder it is given. So the command line names `database/`:

```sh
dotnet run --project src/Haruspex.Cli -- generate tests/fixtures/orders/database --include 'sales.*' --out out
```

## Two kinds of fixture

- **A fixture checked per procedure** (every one but `shop`). Its `expected/` folder holds, for each procedure, its IR
  (`sales.get_order.ir.verified.json`) and its Zod (`sales.get_order.zod.verified.ts`), named for the procedure and what
  they hold; `FixtureSnapshotTests` fails on a procedure without both, a snapshot without a procedure or any other file there. It has a seed and
  runs, and the differential tests deploy it, call every procedure and check every value against the IR.
- **A project for the CLI** (`shop`): its `haruspex.json` names its `source` folder, chooses the procedures and names
  them, so what it must produce is the command's two files, `expected/haruspex.ir.verified.json` and
  `expected/haruspex.zod.verified.ts`, which `FixtureSnapshotTests` checks by running the CLI in the fixture's folder, as a
  user would; anything else in `expected/` fails it. It is not deployed, so it has no seed or runs; its
  README says why.

A folder that lacks a part its kind needs fails discovery, and with it every test that uses a fixture: a fixture can't
be half added and quietly skipped.

## `runs.json`

JSON, with comments and trailing commas allowed (as in `haruspex.json`), so each choice can say why it is there.

```jsonc
{
  // Why the seed is held to every edge, or isn't.
  "seedReachesEveryEdge": true,

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
| `seedReachesEveryEdge` | When `true`, the seed must reach every edge the IR claims: each bound, each full length, each value of a set (`The_seed_reaches_every_edge_the_ir_claims`). For the fixtures about scalar facts. Absent means `false`. |
| `setups` | Named SQL to run before a call. A run with a setup writes, so it runs alone, in a session that is rolled back. `writes` (`SET NOCOUNT ON;`) changes nothing: it is how a run of a procedure that writes asks for that session. |
| `runs` | Each call: a `name` unique in the file (the test shows it after the fixture's name, as in `orders: get_order, no such order`), the `procedure`, its `parameters` and a `setup`. |
| `notRun` | Procedures deliberately not run, each with the reason (`Every_procedure_is_run_or_excluded_with_a_reason`). |

**A parameter's type comes from the procedure.** The file gives each argument as JSON: a number, a string, `true` or
`false`, `null`, or a date as `"2026-11-03"`. The reader converts it to what the procedure declares (`tinyint`, `bigint`,
`date` and the rest), as an `EXEC` would, so the call sends the declared type without the file repeating it. A key, setup,
procedure or parameter the reader doesn't know fails every run, rather than being ignored.

## Adding a fixture

1. Make the folder, with `README.md` and the schema under `database/`. The schemas of all deployed fixtures share one
   database, so pick schema names no other fixture uses.
2. Write `seed.sql`, and the runs in `runs.json`, until the differential tests' coverage passes
   ([their README](../Haruspex.DifferentialTests/README.md#the-seed-must-reach-every-case)).
3. Run the tests: the snapshot tests write each procedure's `*.received.*` files into `expected/`. Check each against what
   SQL Server really does before accepting it as `*.verified.*` ([the snapshot helper](../Haruspex.Tests/Snapshotting/README.md)).

Nothing else names the fixture: the emit check compiles its Zod with `tsc`, and the differential tests deploy, seed and
call it, because they find it.
