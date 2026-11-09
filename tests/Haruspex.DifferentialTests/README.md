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
  SqlClient returns for that kind.

It does not check what the IR leaves open, such as fractional-second precision: that is pinned separately,
as observed fact, by `ForJsonObservationTests`. Closed value sets are not checked yet because IR 0.1 has
none; when the IR gains `values`, the validator should check each value seen is in the set.

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
`Every_procedure_is_run_or_excluded_with_a_reason` makes sure a new fixture procedure is not forgotten.

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
`DisposeAsync` drops it in a `finally` whether the tests passed, failed or never ran, and a failure while
deploying or seeding is reported by each test rather than thrown, so the fixture is still disposed. The one
case this cannot cover is a process that is killed; each run therefore also drops `haruspex_test_*`
databases more than a day old, which leaves a run going on at the same time alone.

## Adding a fixture or a run

Add the fixture's name to `Runs.Fixtures`, write `Seeds/<name>.sql`, and add runs to `Runs.All` until the
coverage test passes. A procedure that cannot run goes in `Runs.NotRun` with the reason.
