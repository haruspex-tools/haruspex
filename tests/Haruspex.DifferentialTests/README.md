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
   empty tables. Such a run writes, so it runs alone (see "Runs that write").
5. **The output is validated against the IR** (`Validation/IrValidator.cs`).
6. **The database is dropped**, in the fixture's `DisposeAsync`, whatever happened before (see below).

## Runs that write

Tests run in parallel against one database. Most only read, but some write inside a transaction that is rolled
back: a run's `Setup` (deleting rows, to see a procedure on empty tables), and the observation tests that create
a table or functions to watch the engine (`CheckConstraintObservationTests`, `JsonFunctionObservationTests`). The
rollback hides what they wrote, but not their locks: until it, a run reading the same tables waits on them, and
when the two take their locks in opposite orders SQL Server kills one with error 1205. GitHub's CI did on
9 Nov 2026 (four tests on `trees`). `IsolationTests.A_run_that_writes_never_overlaps_a_run_that_reads` makes the
overlap certain, holding a setup open while a reading run starts; before the fix the reader waited on `LCK_M_S`
every time.

So the fixture has a **reader–writer gate** (`Server/DatabaseGate.cs`): any number of sessions that read at
once, or one that writes, alone. Nothing reaches the database except through it:

- The fixture keeps the connection string to itself. A test asks for a **`ReadSession`**
  (`server.ReadAsync`) or a **`WriteSession`** (`server.WriteAsync`); both hold their place in the gate until
  disposed. A write session's work is one transaction, always rolled back. `server.ObserveAsync` and
  `server.RunAsync` pick the session from the run: a write session when it has a `Setup`.
- **A read session cannot write**, so a test or procedure that tries fails at once rather than deadlocking
  another test by luck. It reads a **database snapshot** of the test database, taken once it is seeded,
  which is read-only to everyone: even a `dbo` procedure, which ownership chaining lets past every
  permission, fails with error 3906. It also runs as `haruspex_reader`, a database user with
  `db_datareader` and `EXECUTE` only, switched to `WITH COOKIE`, a cookie only the session holds. So a test
  cannot `REVERT` to `dbo` (15591), nor `USE` the writable database, because an impersonated user is confined
  to its own (916). Temporary tables are allowed: they are the session's own, in tempdb. Writes see the
  database itself, and roll back, so readers always see the seed.
- `No_test_opens_a_connection_of_its_own` fails if a file outside the fixture names `SqlConnection`,
  `SqlClientFactory` or `DbProviderFactories`. It is there to catch a mistake, not to stop someone set on
  going around it.
- Deploying and seeding, the one write that is committed, holds the gate too, though no test has started.
- The gate is not re-entrant: a test holding a session that asks for another waits on itself. A wait over
  two minutes fails, naming the likely cause.

Why a gate rather than a database per writing run: the writes are few and short (a dozen setups and a few
dozen observation tests, each well under a second), so making them wait costs little, while a copy per run
means deploying and seeding again, seconds each, or a snapshot per run. Retrying on 1205 or running the
whole suite serially would hide the cause, or slow every read for the sake of a few writes. The one shared
snapshot is not there for isolation (the gate is) but to make writing through a read session impossible;
the drop removes it before the database, killing its sessions, since a read-only database cannot be set to
`SINGLE_USER`.

## The validator reads the IR, not the Zod

The validator is written against the IR directly, never through an emitter, so it cannot share an
emitter's mistakes. It checks what the IR claims:

- a field with no `nullableBecause` is never null;
- every key present is one the type has, at most once, and every key whose presence is `always` is there;
  a key that is `omittedWhenNull` is never present with a null;
- a result without `mayBeAbsent` always comes back (a FOR JSON result with no row is absent, which needs `no-rows`);
- a result returns no more rows than its `cardinality` allows, and exactly one when it is `one` (IR 0.8): a rowset's rows,
  or a FOR JSON result's objects;
- the number of result sets is the number of results, less any trailing ones that may not run (`not-run`, IR 0.6),
  which then count as absent;
- a rowset that is a `union` of arrays (several statements may return it) fits one variant whole;
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

What the statement walk relies on (which result sets arrive when a branch is not taken, a path returns early or an
error is raised; what temporary tables, table variables and `SELECT … INTO` give; how a recursive CTE's columns are
typed) is pinned by `StatementFlowObservationTests`. What following `EXEC` relies on (where a callee's result sets arrive,
what it can do to its caller's temporary tables, what `INSERT … EXEC` accepts, how deep calls nest, which procedure an
`sp_` name runs) is pinned by `ExecObservationTests`. What refinement relies on (which rows a filter keeps when a column is
NULL, under three-valued logic and ANSI_NULLS; what aggregates give over no rows; how many rows TOP, FETCH and a unique key
allow; when COUNT overflows to NULL) is pinned by `RefinementObservationTests`. What APPLY, `VALUES` lists and views rely on
(which outer rows each APPLY keeps, what an applied aggregate gives, what an applied query can see, a `VALUES` column's type,
what a view binds when it is created, its `ANSI_NULLS`, `NOEXPAND`) is pinned by `ApplyViewObservationTests`. What the
expressions rely on (the types of literals, `COALESCE`, `ISNULL`, conversions, `CONCAT`, `+`, decimal arithmetic and `SUM`;
which overflow NULL under a caller's `ANSI_WARNINGS` and `ARITHABORT` off and which conversions cannot overflow; `COALESCE`
reading a subquery twice) is pinned by `ExpressionObservationTests`; what table-valued functions rely on (arguments converted
as assignments are, a multi-statement function's return table enforcing its constraints, `DEFAULT`) by
`TableFunctionObservationTests`; how far a procedure's isolation level reaches by `ReadUncommittedObservationTests`; and what
`CASE`, `IIF`, `CHOOSE`, `NULLIF`, and `COALESCE` and `ISNULL` over JSON rely on (their types, what makes them NULL, how a
branch knows its condition, how each nests in JSON, and when the server folds one) by `CaseObservationTests`. A field whose
only reasons to be null are `count-overflow`, `arithmetic-overflow` or `read-twice` need not be seen null: no seed has
2,147,483,648 rows, the runs use the default session options, under which an overflow is an error, and a COALESCE's second
read differs from its first only when another statement changes the rows between them. They all make their
calls through `Server/ObservedCall.cs`.

`DescribedTypeTests` checks what the validator cannot see in a value: that the SQL type the IR gives each column of every
procedure's first rowset is the one SQL Server describes (`sys.dm_exec_describe_first_result_set_for_object`). A wrong
precision or length passes the value checks as long as the data is short; the type is a claim of its own.

A fixture may have a `haruspex.json` beside its SQL: its IR is then inferred with it applied, so results it declares
(the `calls` fixture's dynamic SQL) are checked against the server like everything inferred.

It does not check what the IR leaves open, such as fractional-second precision: that is pinned separately,
as observed fact, by `ForJsonObservationTests`, and what the JSON constructors write (null options, empty
aggregates, duplicate keys, what nests as JSON, scalar formats) by `JsonConstructorObservationTests`. The engine rules the facets rely on (a string CHECK admits
other cases and trailing spaces, `char(n)` pads to bytes, an alias type's NOT NULL) are pinned the same way by
`CheckConstraintObservationTests`.

## The generated Zod meets the same data

`ZodAcceptanceTests` runs every run's output through the Zod emitted for its procedure, and fails if a parse that
succeeds drops a key the output had (a union whose subset variant read a superset's row), in Node against the
pinned zod (`tests/emit-check/src/accept.mjs`). The validator checks the IR; this checks the emitter turned the
IR into schemas that accept what SQL Server returns. JSON results are handed over as SQL Server's text, so
`JSON.parse` rounds their numbers as an application's would; rows as the mssql driver delivers them (bigint as
a string, dates as `Date`). It needs Node, like the emit check, and `HARUSPEX_REQUIRE_NODE=1` makes its absence a
failure.

A failure names the procedure, the path with array indexes, the claim and the value:

```
sales.list_orders results[0][2].salesperson_name: the IR claims a non-null value, but the value was null
```

`OverrideAcceptanceTests` and `DeclarationAcceptanceTests` show the same for claims a person makes in the config file: a
wrong `nullable: false` or exact value set, and wrong declared results of a dynamic-SQL procedure, are rejected by the
generated Zod on SQL Server's real output, while the right ones are accepted.

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
items when the IR bounds them. A maximum of 255 is not shown to hold by values of 7. `orders` is about query shapes, and its scalars are the types `values` covers at their edges; `flow` (statement flow:
temporary tables, table variables, branches and recursive CTEs) likewise, and `calls` (procedures calling procedures).
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
coverage test passes. A procedure that cannot run goes in `Runs.NotRun` with the reason. A run whose procedure itself writes needs a `Setup` so that it runs in a write
session; in a read session the write fails the run.
