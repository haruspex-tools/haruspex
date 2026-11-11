# Contributing to haruspex

Thank you for considering a contribution. This page says how the project is built and tested, and the
few conventions that keep it trustworthy.

## The principle every change must keep

**Never claim more than the engine guarantees.** A field reported as non-null must never be null, and a
reported shape must hold for every value SQL Server can produce. When a construct is not understood,
the answer is an *unknown with a reason*, never a guess. A user forgives a tool that is sometimes too
cautious, but not one that is confidently wrong, because that error surfaces at runtime in their code.
A change that types more fields by guessing will not be accepted.

## Test first

Every change starts with a failing test.

- **A new SQL construct** gets a small fixture (invented SQL, in `tests/fixtures/`) and a snapshot of the
  IR and of the generated Zod. Write the fixture and the test, run it, and check that the snapshot the
  test proposes (`*.received.*`) is what SQL Server would really do, before accepting it as
  `*.verified.*` (rename it, or rerun with `HARUSPEX_ACCEPT=1`). The snapshot is the claim, so review it
  as carefully as code. The helper is described in `tests/Haruspex.Tests/Snapshotting/README.md`.
- **A bug** gets a test that reproduces it before the fix.
- **A test's name is a sentence** saying what it proves, at least three words joined by underscores
  (`A_null_key_is_left_out`, never `Ir`). `TestNameTests` (in `tests/Shared`) checks every test in both projects.
- Fixtures are invented. Never copy schema or code from a real organisation's database into this
  repository, even in a test.

## Decision context lives with the code

When a line of code is there for a non-obvious reason (an engine rule, a soundness argument, a
compatibility constraint), say why in a short comment beside it, or in a `README.md` in the same folder
when it needs more room. A reviewer should not need the commit history or an issue thread to understand
why the code is the way it is. Comments say *why*, not *what*.

## Building and testing

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). Node.js (LTS) is optional: without it,
the test that compiles the generated TypeScript is skipped, with a message saying so.

```sh
dotnet build
dotnet test
```

The build treats warnings as errors, with nullable reference types and every SDK analyser on
(`AnalysisLevel` `latest-all`). Code style is part of the build too: every style rule is a warning, so a
file that `dotnet format` would change does not build. The house style, and the few rules turned off with the
reason for each, are in `.editorconfig`. Before committing, run:

```sh
dotnet format
```

CI runs `dotnet format --verify-no-changes` after the build, and lints the workflow files with `actionlint`
and `zizmor`.
Every relative link in the repository's Markdown, anchors included, must resolve; `MarkdownLinkTests` checks
them.
Package versions are central (`Directory.Packages.props`) and locked (`packages.lock.json`, committed).
To add or update a package, change `Directory.Packages.props`, run `dotnet restore --force-evaluate`,
and commit the updated lock files with the change. Dependency updates are read by hand, never merged
automatically.

The emit-check test (`tests/emit-check`) writes the Zod generated from the fixtures and compiles it with
`tsc --noEmit` against a pinned `zod`. It runs `npm ci` there the first time. The same package holds
`src/multiple-of.mjs`, the evidence that Zod checks a decimal's step without floating-point false failures, and
`src/accept.mjs`, through which the differential tests run real output into the generated schemas. CI sets
`HARUSPEX_REQUIRE_NODE=1`, which turns a missing Node into a failure rather than a skip.

### Differential tests against SQL Server

`tests/Haruspex.DifferentialTests` deploys the fixtures into a real SQL Server, seeds them, runs every
procedure and checks the output against the inferred IR; its `README.md` explains how. It needs a SQL
Server, found in this order:

1. `HARUSPEX_TEST_SQLSERVER`, a connection string to a server of your own. Each run creates a database
   with a random name (`haruspex_test_…`) and drops it afterwards, even when tests fail, so the login needs
   rights to create and drop databases. **Never point it at a shared server or one holding real data.**
2. Otherwise Testcontainers starts SQL Server in Docker, which needs Docker reachable through a local
   socket, a named pipe or TCP. It cannot use a Docker host reached over SSH.
3. Otherwise these tests are skipped, saying why. CI sets `HARUSPEX_REQUIRE_SQLSERVER=1`, which turns the
   skip into a failure.

SQL Server's images are x64 only, so on an Apple Silicon Mac, or wherever Docker is on another machine, a
long-running server of your own is the simplest route. On any x64 machine with Docker:

```sh
docker run -d --name haruspex-mssql --restart unless-stopped \
  -p 14330:1433 -e ACCEPT_EULA=Y -e MSSQL_PID=Developer \
  -e MSSQL_SA_PASSWORD="$(openssl rand -base64 48 | tr -dc 'A-Za-z0-9' | head -c 32)Aa1" \
  mcr.microsoft.com/mssql/server:2025-CU10-ubuntu-24.04
```

Read the password back with `docker exec haruspex-mssql printenv MSSQL_SA_PASSWORD`, keep it in a file
outside the repository that only you can read, and export the connection string from there before testing:

```sh
export HARUSPEX_TEST_SQLSERVER='Server=<host>,14330;User ID=sa;Password=<password>;Encrypt=true;TrustServerCertificate=true'
```

Use the same image as `SqlServerFixture.Image`, so local runs see what CI sees. `TrustServerCertificate`
is there because the container's certificate is self-signed; use it only for a server of your own.

To run the tool from source:

```sh
dotnet run --project src/Haruspex.Cli -- generate tests/fixtures/orders --include 'sales.*' --out out
cd tests/fixtures/shop && dotnet run --project ../../../src/Haruspex.Cli -- generate --out ../../../out/shop  # with its haruspex.json
```

## Layers

The code is split into layers, one project each, and **a layer refers only to the layers and packages its row
in the table allows**: only `Sources` reads a database project, `Semantics` never sees the IR, the IR and the
emitters never see SQL, and no SQL Server layer knows the emitters. The table, the reasons for its
lines, and the types that were split to fit it are in [docs/architecture.md](docs/architecture.md).

`tests/Haruspex.Tests/ArchitectureTests.cs` enforces it: it holds the same table as data and reads each
assembly's references and each project file's, failing on anything the table doesn't allow. A new layer, or a
new reference between layers, changes the table in both places in the same pull request. Never weaken the
test to let a reference through; move the code to the layer that may know it.

| Path | What it holds |
|---|---|
| `src/Haruspex.Diagnostics` | Messages and source locations, shared by the SQL Server layers. Packable, because Model's API uses it. |
| `src/Haruspex.SqlServer.Model` | The schema as data. Packable; see its `README.md`. |
| `src/Haruspex.SqlServer.Sources` | Reads a folder of `.sql` files into the model with ScriptDom. Packable; see its `README.md`. |
| `src/Haruspex.SqlServer.Semantics` | Binds references in bodies to model objects, and interprets the schema. |
| `src/Haruspex.SqlServer.Inference` | Infers the IR from bound procedure bodies. |
| `src/Haruspex.Ir` | The IR model, its deterministic JSON form, its path syntax and the one identifier rule. Depends on nothing. |
| `src/Haruspex.Config` | The config file (`haruspex.json`): reading it, selection, overrides and naming applied to the IR. See its `README.md`. |
| `src/Haruspex.Emit.Zod` | The Zod emitter. Reads only the IR. |
| `src/Haruspex.Cli` | The `haruspex` command, and `SqlProject`, which composes the layers. |
| `schemas` | The published JSON Schema of `haruspex.json`, checked against examples by `ConfigSchemaTests`. |
| `tests/Haruspex.Tests` | Unit, snapshot, end-to-end and architecture tests. |
| `tests/Haruspex.DifferentialTests` | Runs the fixtures in SQL Server and checks the output against the IR and the generated Zod. |
| `tests/Shared` | Test helpers compiled into both test projects: running the emit-check npm package. |
| `tests/fixtures` | Invented SQL used by the tests. |
| `tests/emit-check` | A small npm package that type-checks the generated Zod. |

Only `Model` and `Sources` are packable (`IsPackable`, with package metadata and a readme), with `Diagnostics`
because its types are in their public API; every other project is not. Nothing is published yet: that is a later decision, so don't push packages anywhere. Their public API
must be documented, which the build enforces: a public member without a comment is an error (`CS1591`) in
them and in the layers reusable enough to follow them (`Semantics` and `Ir`). A comment says what the member is
for, not its name again.

## Dependencies

Every dependency is listed here with its licence and the reason it is needed. A new one needs the same, and
the maintainer's agreement.

| Package | Licence | Used by | Why |
|---|---|---|---|
| `Microsoft.SqlServer.TransactSql.ScriptDom` | MIT | Model, Sources, Semantics, Inference | Microsoft's own T-SQL parser: the only way to read T-SQL as SQL Server does. |
| `System.CommandLine` | MIT | CLI | Argument parsing, help and usage errors, maintained by the .NET team. |
| `xunit.v3` | Apache-2.0 | tests | The test framework; runs on Microsoft.Testing.Platform (opted into in `global.json`). |
| `typescript` (npm) | Apache-2.0 | emit-check | Compiles the generated Zod, to prove it type-checks. |
| `zod` (npm) | MIT | emit-check | The library the generated code targets, pinned so the check is repeatable. |
| `Microsoft.Data.SqlClient` | MIT | differential tests | Microsoft's SQL Server driver for .NET: the tests must run procedures and read their results as SQL Server returns them. |
| `Testcontainers.MsSql` | MIT | differential tests | Starts a throwaway SQL Server in Docker when no server is configured, as in CI; it can later run a matrix of SQL Server versions. |
| `System.Security.Cryptography.Pkcs` | MIT | differential tests | Not used directly: pinned to a patched version because the one SqlClient brings has two advisories (see `Directory.Packages.props`). |

These three are used only by the differential tests, and nothing in them is shipped. Their transitive
packages are MIT, with one exception: `Microsoft.Data.SqlClient.SNI.runtime`, SqlClient's native network
layer for Windows, is under the Microsoft Software License Terms. They allow it to be used freely "to develop
and test your applications", which is all the tests do; like DacFx's, they ask anyone who *redistributes* it to
indemnify Microsoft. So it must never reach anything haruspex ships: the shipped projects under `src/` must
not reference SqlClient.

ScriptDom (MIT) is the only SQL dependency. **DacFx must not be added**: its licence requires anyone who
redistributes it to indemnify Microsoft, which no maintainer of a free tool should take on. DACPACs will
be read directly from their `model.xml`. Snapshot tests use a small helper in the repository rather than a
snapshot library; `tests/Haruspex.Tests/Snapshotting/README.md` says why.

## Review of agent and CI configuration

`.github/CODEOWNERS` requires the maintainer's review for changes to `.claude/`, `.github/`, `AGENTS.md`
and `CLAUDE.md`. Agent instructions and hooks run on contributors' machines, and workflows run in CI with
the repository's permissions, so a change to them is a change to what runs on other people's computers.

## Licence

By contributing you agree that your contribution is licensed under the [Apache License 2.0](LICENSE).
