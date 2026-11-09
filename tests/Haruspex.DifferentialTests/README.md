# Differential tests

These tests run the fixtures in a real SQL Server and check what comes back against the IR that haruspex
inferred for them. They are how "never claim more than the engine guarantees" is enforced mechanically: a
field the IR calls non-null that SQL Server returns as null is a soundness bug, found by a test rather than
by a user at runtime.

## What happens in a run

1. **A server is found** (below), and one database is created for the run, named `haruspex_test_<random>`.
2. **Each fixture in `Runs.Fixtures` is deployed** into it: every batch of every `.sql` file under
   `tests/fixtures/<name>`, retried after the others when it fails, because a fixture's scripts are in no
   particular order (a foreign key may name a table created later). The fixtures' schemas must not overlap.
3. **Each fixture is seeded** from `Seeds/<name>.sql`.
4. **Each run in `Runs.All` calls a procedure** and reads every result set. A run with `Setup` runs that SQL
   first in a transaction that is rolled back, which is how a procedure without parameters is run against
   empty tables.
5. **The output is validated against the IR** (`Validation/IrValidator.cs`).
6. **The database is dropped**, in the fixture's `DisposeAsync`, whatever happened before (see below).

## The validator reads the IR, not the Zod

The validator is written against the IR directly, never through an emitter, so it cannot share an
emitter's mistakes. It checks what the IR claims:

- a field with no `nullableBecause` is never null;
- every key present is one the type has, at most once, and every key whose presence is `always` is there;
  a key that is `omittedWhenNull` is never present with a null;
- a result without `mayBeAbsent` always comes back (a FOR JSON result with no row is absent);
- the number of result sets is the number of results;
- every scalar has the form its kind claims: in JSON, integers within their format's range, dates, times,
  date-times (with or without an offset) and GUIDs as strings of the right form; in a rowset, the .NET type
  SqlClient returns for that kind;
- a tuple has exactly its positions, an array's items are null only when the IR says they may be and their number
  is within its bounds, a record's every value has its type (any key, even repeated), and a union's value is one of
  its variants (IR 0.3);
- JSON text with a `content` parses to a value of that type, checked at `<path><json>` (IR 0.4), in JSON and in a
  rowset; a recursive type is checked at every depth the data reaches (IR 0.4);
- every scalar keeps its facets (IR 0.2): a length in its unit (UTF-16 code units, or UTF-8 bytes when the IR
  says so, and otherwise only as an upper bound), a minimum length, digits, bounds compared exactly from the
  decimal text (never through a double), and value sets. An `exact` set is matched exactly; a `collation` set
  ignoring case and trailing spaces, which is what the fixtures' collations ignore.

It does not check what the IR leaves open, such as fractional-second precision: that is pinned separately,
as observed fact, by `ForJsonObservationTests`, and what the JSON constructors write (null options, empty
aggregates, duplicate keys, what nests as JSON, scalar formats) by `JsonConstructorObservationTests`. The engine rules the facets rely on (a string CHECK admits
other cases and trailing spaces, `char(n)` pads to bytes, an alias type's NOT NULL) are pinned the same way by
`CheckConstraintObservationTests`.

## The generated Zod meets the same data

`ZodAcceptanceTests` runs every run's output through the Zod emitted for its procedure, in Node against the
pinned zod (`tests/emit-check/src/accept.mjs`). The validator checks the IR; this checks the emitter turned the
IR into schemas that accept what SQL Server returns. JSON results are handed over as SQL Server's text, so
`JSON.parse` rounds their numbers as an application's would; rows as the mssql driver delivers them (bigint as
a string, dates as `Date`). It needs Node, like the emit check, and `HARUSPEX_REQUIRE_NODE=1` makes its absence a
failure.

A failure names the procedure, the path with array indexes, the claim and the value:

```
sales.list_orders results[0][2].salesperson_name: the IR claims a non-null value, but the value was null
```

## The seed must reach every case

A non-null claim is only tested by data that could break it. So the validator also records what it saw at
each path, and `The_seed_reaches_every_case_the_ir_allows` fails unless, across a fixture's runs, every
nullable field was seen null, every field was seen with a value, and every result that may be absent was
seen absent. A nested object that FOR JSON leaves out because all its values are null counts as each of its
fields being null: the columns of a left join are null together, so they are never seen null one at a time.
The same holds inside what the constructors build: each position of a tuple, each record's values and each array
whose items may be null must be seen null (when they can be) and with a value. Tuple positions are paths of their
own (`v[0]`, `v[1]`), a record's values share `v{}`, and a union's variants share their path; a value counts as seen
for every variant it satisfies.

For the fixtures about scalar facts (`Runs.EdgeFixtures`: `values`, `checks`, `payloads` and `trees`), `The_seed_reaches_every_edge_the_ir_claims`
also fails unless the data reached every edge of every facet: each bound (or, when exclusive, the nearest step
inside it), each full length, each minimum length and each value of each set, and each array's fewest and most
items when the IR bounds them. A maximum of 255 is not shown to hold by values of 7. `orders` is about query shapes, and its scalars are the types `values` covers at their edges.
`Every_procedure_is_run_or_excluded_with_a_reason` makes sure a new fixture procedure is not forgotten.

A recursive type's cases are counted by its name, not its path: every field of `ComponentsNode` is recorded as
`<ComponentsNode>.field`, whatever the depth, so a leaf four levels down covers the "null" case, and the walk over the
IR ends. What JSON text parses to is a path of its own, `<path><json>`, and needs its cases like any other.

## Where SQL Server comes from

In order:

1. **`HARUSPEX_TEST_SQLSERVER`**, a connection string. The login needs to create and drop databases. Use a
   server of your own; never a shared server holding anyone's data.
2. **Testcontainers**, which starts `mcr.microsoft.com/mssql/server` at the version pinned (by tag and digest)
   in `SqlServerFixture.Image`, when Docker is reachable through a local socket, a named pipe or TCP.
3. **Neither**: every test that needs a server is skipped, and the skip says why. With
   `HARUSPEX_REQUIRE_SQLSERVER=1`, as CI sets it, the skip becomes a failure.

**Testcontainers cannot use a Docker host reached over SSH** (a Docker context such as `ssh://host`).
Checked on 9 Nov 2026 with Testcontainers 4.16.0 and `DOCKER_HOST=ssh://…`: Docker.DotNet throws
`SshDockerEndpointNotSupportedException` ("Docker endpoints using SSH are not supported"). Testcontainers
does depend on SSH.NET, but for forwarding host ports into containers, not for reaching Docker. Where Docker
is only reachable over SSH, run a long-lived SQL Server container there and use the first route.

## Why the database is dropped in a `finally`

A developer's server is long-running and every run creates a database on it. If a failing run left its
database behind, they would pile up, and an old one could be mistaken for the current run's. So
`DisposeAsync` drops it in a `finally` whether the tests passed, failed or never ran. A failure while
deploying or seeding is reported by each test rather than thrown, so the fixture is still disposed. Only a
database the run actually created is dropped: when the server cannot be reached there is nothing to drop,
and a drop that fails says so under its own message.

Stopping a run must not leave it behind either:

- **SIGINT** (Ctrl+C) cancels the run, which still reaches `DisposeAsync`.
- **SIGTERM** (an IDE's stop button, `timeout`, `docker stop`) does not always reach it, so a SIGTERM handler
  and process exit drop the database too. The three share one drop and each waits for it, because xunit
  carries on after SIGTERM and a process that exited during another path's drop would leave the database.
  The handlers are in place before the `CREATE DATABASE` is sent, and wait for it to finish, so a SIGTERM
  while it runs is covered. The drop retries, because tests still running can take the database between
  `SET SINGLE_USER` and `DROP`. Checked on 9 Nov 2026 by sending SIGTERM at fifteen points from 0.5 to 2.0 s
  into a run, SIGINT at five, and `timeout` around `dotnet test`: no database was left.
- **SIGKILL** cannot be caught. The next run lists every `haruspex_test_*` database it did not create as a
  skipped test, `Earlier_runs_left_no_database_behind`, whose reason names them; a skip's reason is printed
  by `dotnet test`, where xunit's warnings are not. It is not a failure, because the database may belong to
  another run going on at the same time. Databases more than a day old are dropped at the start of a run.

## Adding a fixture or a run

Add the fixture's name to `Runs.Fixtures`, write `Seeds/<name>.sql`, and add runs to `Runs.All` until the
coverage test passes. A procedure that cannot run goes in `Runs.NotRun` with the reason.
