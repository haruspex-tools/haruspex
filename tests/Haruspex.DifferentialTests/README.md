# Differential tests

These tests run the fixtures in a real SQL Server and check what comes back against the IR that haruspex
inferred for them. They are how "never claim more than the engine guarantees" is enforced mechanically: a
field the IR calls non-null that SQL Server returns as null is a soundness bug, found by a test rather than
by a user at runtime.

## What happens in a run

1. **A server is found** (below), when the first test that needs one asks for it (`SqlServerFixture.RequireAsync`), and one
   database is created for the run, named `haruspex_test_<random>`. A run of tests that need no server, such as the
   validator's own (`Validation/`) or the pool's (`Server/RoomPoolTests.cs`), starts nothing and creates nothing.
2. **Each fixture with a seed is deployed** into it (`Runs.Fixtures`, found by listing `tests/fixtures`, whose
   [README](../fixtures/README.md) describes a fixture's folder): every batch of every `.sql` file under its
   `database/` folder, retried after the others when it fails, because a fixture's scripts are in no particular order (a
   foreign key may name a table created later). The fixtures' schemas must not overlap.
3. **Each fixture is seeded** from its `seed.sql`.
4. **Each run in `Runs.All` calls a procedure** and reads every result set. The runs are read from each fixture's
   `runs.json`, which gives each parameter as JSON and leaves its type to the procedure's declaration, as an `EXEC` does. A run with `ProcedureRun.Setup` runs that SQL
   first in a transaction that is rolled back, which is how a procedure without parameters is run against
   empty tables. Such a run writes, so it runs alone, in a copy of the database (see "Runs that write").
5. **The output is validated against the IR** (`Validation/IrValidator.cs`).
6. **The databases are dropped**, in the fixture's `DisposeAsync`, whatever happened before (see below).

## Runs that write

Tests run in parallel. Most only read, but some write inside a transaction that is rolled back: a run's
`ProcedureRun.Setup` (deleting rows, to see a procedure on empty tables), and the observation tests that create a table,
a type or a procedure to watch the engine (most of them, through `Server/ObservedCall.cs`). The rollback hides what they
wrote, but not their locks: until it, a run reading the same tables waits on them, and when the two take their locks in
opposite orders SQL Server kills one with error 1205. GitHub's CI did on 9 Nov 2026 (four tests on `trees`). Two writers
creating the same `dbo.obs_…` procedure would wait on each other the same way.

So **no session that writes shares a database with any other session**:

- The fixture keeps the connection strings to itself. A test asks for a **`ReadSession`** (`server.ReadAsync`) or a
  **`WriteSession`** (`server.WriteAsync`). `server.ObserveAsync` and `server.RunAsync` pick the session from the run: a
  write session when it has a `ProcedureRun.Setup`.
- **Read sessions share the test database's snapshot.** A database snapshot, taken once the database is seeded, is
  read-only to everyone, and nothing writes the database itself after that, so readers run side by side and never wait
  on a writer. A read session cannot write either: even a `dbo` procedure, which ownership chaining lets past every
  permission, fails with error 3906. It also runs as `haruspex_reader`, a database user with `db_datareader` and `EXECUTE`
  only, switched to `WITH COOKIE`, a cookie only the session holds. So a test cannot `REVERT` to `dbo` (15591), nor `USE`
  the writable database, because an impersonated user is confined to its own (916). Temporary tables are allowed: they
  are the session's own, in tempdb.
- **Each write session has a database of its own while it lasts**, a room (`Server/RoomPool.cs`): a copy of the test
  database, made by the same deploy, seed, reader and probe (`SqlServerFixture.PrepareAsync`), so a writer sees what a
  reader does. A room is made the first time every room so far is taken, up to one a processor (xunit runs that many tests
  at once), and is handed to the next write session once the last has rolled back. The mssql driver in Node, which
  connects by itself, takes a room the same way (`SqlServerFixture.DriverAsync`).
- `No_test_opens_a_connection_of_its_own` fails if a file outside the fixture names `SqlConnection`,
  `SqlClientFactory` or `DbProviderFactories`. It is there to catch a mistake, not to stop someone set on
  going around it.
- A test that holds as many write sessions as there are rooms and asks for another waits on itself; a wait over two
  minutes fails, naming the likely cause.

`IsolationTests.A_run_that_writes_never_shares_a_database_with_a_run_that_reads` holds a writing run's setup open while a
reading run of the same tables runs to its end, waiting on no lock of it.

Why a room per writer rather than one gate: until 25 Nov 2026 every write session took the one database alone and waited
for every reader, on the reasoning that writes were "a dozen setups and a few dozen observation tests". By then there
were 111 runs with a setup and some 600 observation tests, every call of which writes, so writers held the database for
nearly the whole run and the suite ran almost serially (audit finding F049). Deploying and seeding a copy takes seconds,
once per room, and a run makes no more rooms than it has writers at once. Measured on a 10-core x86-64 machine under
WSL, the whole project, back to back with a load average of 8 to 13 from other work: 7 min 3 s with the gate, using 69%
of one core, and 2 min 52 s with rooms, using 183%; under a load of 17 to 40, 12 min 55 s and 3 min 46 s. A run of the
tests that need no server (the 165 in `Validation/`) took 6.6 to 8.8 s while the fixture set up the server for them
first, and takes 0.5 s.

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
- every scalar keeps its facets (IR 0.2): a length in its unit (UTF-16 code units, code points, or UTF-8 bytes when the
  IR says so, and otherwise only as an upper bound), a binary value's length in bytes (exact when fixed), a minimum
  length in its unit (UTF-16 code units or code points), digits, bounds compared exactly from the decimal text (never
  through a double), and value sets. An `exact` set is matched exactly; a `padded` one after removing trailing spaces; a
  `collation` set ignoring case and trailing spaces, which is what the fixtures' collations ignore; and a `canonical`
  set through the document's fold for it;
- any JSON value narrowed to an object or an array is one, and a date-time's offset is within -14:00 to +14:00.

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

A fixture may have a `haruspex.json` beside its `database/` folder: its IR is then inferred with it applied, so results it declares
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
a string, dates as `Date`). A JSON result with no row is handed over as `null`, an array as much as an object, as the
driver does. It needs Node, like the emit check, and `HARUSPEX_REQUIRE_NODE=1` makes its absence a failure.

Each output also goes through the same IR emitted with strict objects (`ZodObjects.Strict`), which accepts only what the default
module does and refuses a key it doesn't list. The check of dropped keys can't see inside JSON text, whose content a
refinement parses and throws away, so without it a key the schema names wrongly there, left out when null, would pass.
`A_key_named_wrongly_fails_the_strict_zod` plants such names in the `meters` fixture and shows only the strict module
refuses them. `JsonSchemaAcceptanceTests` validates its strict document for the same reason.

The driver itself is called too, through `tests/emit-check/src/driver.mjs` with the mssql version that package pins.
`MssqlDriverObservationTests` records what it hands an application for each form of JSON, with its `parseJSON` option on and
off, and `MssqlDriverAcceptanceTests` creates procedures returning each form, then parses what an application reads from the
driver with the schemas emitted for them ([decision 0048](../../docs/decisions/0048-an-absent-json-result-reads-as-null.md)).
The script connects by itself, so it holds a database alone (`SqlServerFixture.DriverAsync`, a room as a write session holds
one) and runs everything in a transaction it rolls back.

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

A facet is only tested by data at its edge: a maximum of 255 is not shown to hold by values of 7. So
`The_seed_reaches_every_edge_its_types_alone_do_not_give` fails unless, across every deployed fixture's runs, the data
reached each edge of a fact the type alone doesn't give (`EdgeScope`): each bound (or, when exclusive, the
nearest step inside it), full length and minimum length whose source is a CHECK, an expression's literals, the engine or a
declaration, and each value of each set. That is where a fact travels with nothing else to hold it: a CHECK's bound carried
into JSON or a second result set, a declared length inside JSON. A bound or length the type gives is held at its edges by
`BuiltInTypeObservationTests` and the expression observation tests, and for a procedure's first rowset by
`DescribedTypeTests`. An edge no run can reach (a CHECK's set wider than the values a procedure writes) is excused in the
fixture's `edgesNotReached` with the reason, and an excuse that names no missing edge fails, so none outlives its reason.

For a fixture whose `runs.json` sets `seedReachesEveryEdge` (`Runs.EdgeFixtures`: `checks`, `payloads`, `trees` and
`values`; each says why), `The_seed_reaches_every_edge_the_ir_claims` also fails unless the data reached every edge of every
facet, the type's too, and each array's fewest and most items when the IR bounds them, and a result of at most one row
returned one.
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
   If the container stops or isn't ready within five minutes, a new one is started, once (`ContainerStart` says why
   once). Each failed attempt's exit code and log are printed: with the first test that reports the failure, in the
   run's error output, and, when the retry succeeds, in the output of the test that started the server (shown with
   `--output detailed`).
3. **Neither**: every test that needs a server is skipped, and the skip says why. With
   `HARUSPEX_REQUIRE_SQLSERVER=1`, as CI sets it, the skip becomes a failure.

**Testcontainers cannot use a Docker host reached over SSH** (a Docker context such as `ssh://host`).
Checked on 9 Nov 2026 with Testcontainers 4.16.0 and `DOCKER_HOST=ssh://…`: Docker.DotNet throws
`SshDockerEndpointNotSupportedException` ("Docker endpoints using SSH are not supported"). Testcontainers
does depend on SSH.NET, but for forwarding host ports into containers, not for reaching Docker. Where Docker
is only reachable over SSH, run a long-lived SQL Server container there and use the first route.

### A server with full-text search

`FullTextObservationTests` needs full-text search, which the image above doesn't have. It runs on a server
`HARUSPEX_TEST_SQLSERVER_FULLTEXT` names, a connection string to one of your own, such as a container of the image
[`Server/FullText`](Server/FullText/README.md) builds, and creates a database of its own (`haruspex_test_…`) for each test,
dropped when the test ends. Without one, or on a server without full-text search, its tests are skipped, saying why;
`HARUSPEX_REQUIRE_FULLTEXT=1` makes the skip a failure. Testcontainers doesn't start it: the image is built from a Dockerfile in
the repository, which a run shouldn't do each time.

### A server that calls a REST endpoint

`RestEndpointObservationTests` needs a server with `external rest endpoint enabled` on, which is off by default, and an HTTPS
endpoint it trusts. It runs on a server `HARUSPEX_TEST_SQLSERVER_REST` names, such as the one [`Server/Rest`](Server/Rest/README.md)
starts beside its endpoint, and creates nothing there. Without one, its tests are skipped, saying why;
`HARUSPEX_REQUIRE_REST=1` makes the skip a failure.

## Why the database is dropped in a `finally`

A developer's server is long-running and every run creates databases on it: the test database and its rooms. If a
failing run left them behind, they would pile up, and an old one could be mistaken for the current run's. So
`DisposeAsync` drops them in a `finally` whether the tests passed, failed or never ran. A failure while
deploying or seeding is reported by each test rather than thrown, so the fixture is still disposed. Only
databases the run actually created are dropped: when no test asked for the server, or it cannot be reached, there is
nothing to drop, and a drop that fails says so under its own message.

Stopping a run must not leave it behind either:

- **SIGINT** (Ctrl+C) cancels the run, which still reaches `DisposeAsync`.
- **SIGTERM** (an IDE's stop button, `timeout`, `docker stop`) does not always reach it, so a SIGTERM handler
  and process exit drop the databases too. The three share one drop and each waits for it, because xunit
  carries on after SIGTERM and a process that exited during another path's drop would leave a database.
  The handlers are in place before the first `CREATE DATABASE` is sent, each database is named before its `CREATE` is,
  and the drop waits for every `CREATE` under way and makes no more rooms, so a SIGTERM while one runs is covered. The
  drop retries, because tests still running can take the database between `SET SINGLE_USER` and `DROP`. Checked on 9 Nov
  2026 by sending SIGTERM at fifteen points from 0.5 to 2.0 s into a run, SIGINT at five, and `timeout` around
  `dotnet test`: no database was left. Checked again on 25 Nov 2026, with rooms and a lazy start, by sending SIGTERM at eight
  points from 1 to 70 s into a run (`timeout -s TERM`): no database was left. The first try left rooms and scratch
  databases behind: a room could be named after the drop had taken its list, and `SqlServerFixture.ScratchAsync`'s
  databases weren't on it. Now naming a database and the drop's list are one step, and every database the run creates is
  on it.
- **SIGKILL** cannot be caught. The next run lists every `haruspex_test_*` database it did not create as a
  skipped test, `Earlier_runs_left_no_database_behind`, whose reason names them; a skip's reason is printed
  by `dotnet test`, where xunit's warnings are not. It is not a failure, because the database may belong to
  another run going on at the same time. Databases more than a day old are dropped at the start of a run.

## Adding a fixture or a run

A new folder under `tests/fixtures` is found by every test that needs a fixture; [its README](../fixtures/README.md) says
what the folder holds. Write its `seed.sql`, and add runs to its `runs.json` until the coverage test passes. A procedure
that cannot run goes in the file's `notRun` with the reason. A run whose procedure itself writes needs a `setup` (the
`writes` one changes nothing) so that it runs in a write session; in a read session the write fails the run.
