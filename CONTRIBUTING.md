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

- **A new SQL construct** gets a small fixture (invented SQL, a folder in `tests/fixtures/` laid out as
  [its README](tests/fixtures/README.md) says) and a snapshot of the IR and of the generated Zod, in the fixture's `expected/` folder. Write the fixture and the test, run it, and check that the snapshot the
  test proposes (`*.received.*`) is what SQL Server would really do, before accepting it as
  `*.verified.*` (rename it, or rerun with `HARUSPEX_ACCEPT=1`). The snapshot is the claim, so review it
  as carefully as code. The helper is described in `tests/Haruspex.Tests/Snapshotting/README.md`.
- **A bug** gets a test that reproduces it before the fix.
- **A test's name is a sentence** saying what it proves, at least three words joined by underscores
  (`Separates_absent_keys_from_null_values`, never `Ir`). `TestNameTests` (in `tests/Shared`) checks every test in both projects.
- Fixtures are invented. Never copy schema or code from a real organisation's database into this
  repository, even in a test.

## Decision context lives with the code

When a line of code is there for a non-obvious reason (an engine rule, a soundness argument, a
compatibility constraint), say why in a short comment beside it, or in a `README.md` in the same folder
when it needs more room. A reviewer should not need the commit history or an issue thread to understand
why the code is the way it is. Comments say *why*, not *what*.

A design decision with alternatives worth recording gets a record in [the decision log](docs/decisions/README.md), and
the code links it. An idea a reader needs in order to understand a layer is explained in
[the guide](docs/internals/README.md), and a change to the code it describes updates its chapter in the same pull request.

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
`tsc --noEmit` (strict) against a pinned `zod`, and `tests/emit-lint` lints the same modules with the recommended rules of
ESLint and typescript-eslint, failing on any warning. Each runs `npm ci` in its folder the first time. The lint has a
package of its own because typescript-eslint reads TypeScript through the compiler's JavaScript API, which the TypeScript
the emit check compiles with no longer has. The example application, `examples/greenhouse`, is compiled the same way
against the module committed beside it (`ExampleAppTests`). The same package holds
`tests/emit-check/src/multiple-of.mjs`, the evidence that Zod checks a decimal's step without floating-point false
failures, and `tests/emit-check/src/accept.mjs`, through which the differential tests run real output into the generated schemas. CI sets
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
dotnet run --project src/Haruspex.Cli -- generate tests/fixtures/orders/database --include 'sales.*' --out out
cd tests/fixtures/shop && dotnet run --project ../../../src/Haruspex.Cli -- generate --out ../../../out/shop  # with its haruspex.json
```

### Coverage on the sample databases

`tests/corpus` holds two of Microsoft's sample databases, vendored at a pinned commit, and `CorpusFiguresTests` writes what
haruspex makes of them to [docs/corpus-figures.md](docs/corpus-figures.md). A change that types more or less of them fails
that test until the page is rewritten with `HARUSPEX_ACCEPT=1`; commit the page with the change, and say in the commit why
the figures moved. A figure in any other public text is copied from that page, never measured elsewhere
([decision 0020](docs/decisions/0020-public-figures-come-from-public-corpora.md)). How the corpora are kept and refreshed
is in [tests/corpus/README.md](tests/corpus/README.md).

## Writing

Everything a reader reads (the Markdown, XML documentation, comments and diagnostics) follows the house style in
[docs/style.md](docs/style.md): British spelling, plain words, sentence-case headings, the project's fixed terms.
Tools check the rules its last table assigns to them: cspell the spelling, Vale the style, and Mermaid that every
diagram parses. Run all three before committing a change to prose:

```sh
tests/prose/check.sh
```

Two tests hold the guide, the decision log and the READMEs to the code: every code reference in them must resolve
(`CodeReferenceTests`), and every worked example is run and its output compared (`WorkedExampleTests`). The conventions
they check are in [the guide's index](docs/internals/README.md#how-the-guide-is-kept-true).

It needs Node, and Vale 3.24.0 on the `PATH` or Docker with a local socket to run Vale's image. CI runs the same
script. [tests/prose/README.md](tests/prose/README.md) says what to do when a check is wrong rather than the text:
a real term goes in the project dictionary, never a misspelling.

## Where a construct's tests are

The tests are laid out like the code. `tests/Haruspex.Tests` has a folder per layer, named as the layer is
(`tests/Haruspex.Tests/Inference`, `tests/Haruspex.Tests/Emit.Zod` and the rest), beside folders for the tests of the
repository itself: its architecture, its documentation, the corpus, the fixtures and the snapshot helper. One project
with a folder per layer, rather than a test project per layer: the Inference tests, the most numerous, reach their layer
through `SqlProject` and the fixtures, which compose every layer, and one project builds and runs them once. The
architecture test, not the project boundary, is what keeps the layers apart. Outside Inference a test class is named for
the type it tests (`ConfigReaderTests` tests `ConfigReader`). Inside Inference it is named for a **construct family**,
the group of SQL constructs one part of the inference handles, and that one name leads to everything else about it:

| What | Where | Named |
|---|---|---|
| Unit tests | `tests/Haruspex.Tests/Inference` | `<Family>Tests`; when the class is large, one partial part per construct, named for it: `ExecTests.InsertExec.cs`, and the family's checks of the IR and Zod it writes, as a whole, in a part of their own, `ExecTests.Zod.cs` or `RefinementTests.IrAndZod.cs` |
| Observation tests, one engine rule each | `tests/Haruspex.DifferentialTests` | `<Family>ObservationTests`, each test named for the rule it observes |
| The reference | `src/Haruspex.SqlServer.Inference/README.md` | a section per construct, which names its code and its observation tests |
| The fixture | `tests/fixtures/<domain>` | for its invented domain (`calls` is a bakery); its README's first sentence names its constructs |

So a rule's unit test is found from the construct's name, and the rule's observation from the unit test's class. A new
construct joins its family's classes, or starts a family with all four. A class of either kind over about 400 lines is
split into parts along the constructs it covers. The families today:

| Family | Unit tests | Observation tests | Fixture | Reference section |
|---|---|---|---|---|
| Statement flow | `StatementFlowTests` | `StatementFlowObservationTests` | `tests/fixtures/flow` | "The state", "Result sets are positional", "CTEs" |
| Calls | `ExecTests` | `ExecObservationTests` | `tests/fixtures/calls` | "Calls" |
| Refinement | `RefinementTests` | `RefinementObservationTests` | `tests/fixtures/refine` | "Refinement" |
| Writes | `DmlTests` | `DmlObservationTests` | `tests/fixtures/dml` | "DML", "Triggers", "Cursors" |
| APPLY and views | `ApplyViewTests` | `ApplyViewObservationTests` | `tests/fixtures/apply` | "APPLY", "Views" |
| Expressions | `ExpressionTests` | `ExpressionObservationTests` | `tests/fixtures/tabs` | "Expressions" |
| Numbers | `NumericTests` | `NumericObservationTests`, `ArithmeticGridTests` | `tests/fixtures/ledger` | "Decimal arithmetic", "Numeric functions" |
| CASE and its kin | `CaseTests` | `CaseObservationTests` | `tests/fixtures/cases` | "CASE, IIF, CHOOSE and NULLIF", "COALESCE and ISNULL over JSON" |
| Built-in functions | `FunctionTests` | `FunctionObservationTests` | `tests/fixtures/funcs` | "Built-in functions", "Window functions" |
| Table-valued functions | `TableFunctionTests` | `TableFunctionObservationTests` | `tests/fixtures/tabs` | "Table-valued functions" |
| Set operations and joins | `SetJoinTests` | `SetJoinObservationTests` | `tests/fixtures/sets` | "Set operations", "CROSS JOIN and FULL OUTER JOIN", "OPENJSON" |
| Combining types | `SetTypeTests` | `SetTypeObservationTests`, `SetTypeGridTests` | `tests/fixtures/bazaar` | "Combining types" |
| JSON traced | `JsonTracingTests` | `JsonTracingObservationTests` | `tests/fixtures/kits` | "JSON built here, traced", "OPENJSON" |
| JSON constructors | `JsonConstructorTests` | `JsonConstructorObservationTests` | `tests/fixtures/payloads` | "JSON constructors" |
| Functions that return JSON | `JsonFunctionTests` | `JsonFunctionObservationTests` | `tests/fixtures/trees` | the guide's [Summaries and recursion](docs/internals/07-summaries-and-recursion.md) |
| Read uncommitted | `ReadUncommittedTests` | `ReadUncommittedObservationTests` | `tests/fixtures/tabs` | [decision 0016](docs/decisions/0016-visible-read-uncommitted-drops-key-claims.md) |

Four classes are named otherwise, for what they cover across families: `ConstructTests` (queries, results and sources
in general, with `FOR JSON`'s rules in `ForJsonObservationTests` and `tests/fixtures/orders`), `FactFlowTests` (facts a
value carries through expressions), `ValueEqualityTests` (the laws equality of values and shapes keeps, over a generated
sample) and, in Semantics, `SchemaFactsTests` (what types, alias types and CHECK constraints
guarantee, observed in `CheckConstraintObservationTests` on `tests/fixtures/checks`). `OPENJSON` belongs to two families: as
a source of rows with its `WITH` columns (`SetJoinTests.OpenJson.cs`), and reading JSON a procedure built and traced
(`JsonTracingTests.OpenJsonAndJsonValue.cs`). `CodeReferenceTests` checks that every name in these tables exists, and
`ConstructFamilyTests` that every Inference test class and every observation or grid class is named here.

## Layers

The code is split into layers, one project each, and **a layer refers only to the layers and packages its row
in the table allows**: only `Sources` reads a database project, `Semantics` never sees the IR, the IR and the
emitters never see SQL, and no SQL Server layer knows the emitters. The table, the reasons for its
lines, and the types that were split to fit it are in [docs/architecture.md](docs/architecture.md).

`tests/Haruspex.Tests/Architecture/ArchitectureTests.cs` enforces it: it reads the table in `docs/architecture.md`, which is its only
copy, and each assembly's references and each project file's, failing on anything the table doesn't allow. A new layer,
or a new reference between layers, is a change to that table's row, nowhere else. Never weaken the test to let a
reference through; move the code to the layer that may know it.

**Each file holds one type and is named after it** (`Table.cs` holds `Table`), so a type's name says where it is.
Nested types stay in the type they belong to, and a partial type's parts are named after the type and the part
(`ProcedureAnalyzer.Flow.cs`).
`tests/Haruspex.Tests/Architecture/SourceLayoutTests.cs` enforces it.

| Path | What it holds |
|---|---|
| `src/Haruspex.*` | The layers, one project each. What each knows and may refer to is in [the table in docs/architecture.md](docs/architecture.md#the-table); each project's `README.md` describes it. |
| `schemas` | The published JSON Schemas: of `haruspex.json`, checked against examples by `ConfigSchemaTests`; and of the IR's file, generated from the model by `IrJsonSchema` and never edited by hand (`IrJsonSchemaTests` fails when it is stale; rewrite it with `HARUSPEX_ACCEPT=1`). |
| `tests/Haruspex.Tests` | Unit, snapshot, end-to-end and architecture tests, a folder per layer ("Where a construct's tests are", above). |
| `tests/Haruspex.DifferentialTests` | Runs the fixtures in SQL Server and checks the output against the IR and the generated Zod. |
| `tests/Shared` | Test support compiled into both test projects (`Haruspex.TestSupport`): the fixtures, found by listing their folder (`Fixture`); inferring procedures from SQL a test writes (`InferProcedure`) and finding things in the IR (`IrNavigation`); temporary folders, the repository's paths, and running the emit-check npm package. |
| `tests/fixtures` | The fixtures, one folder each: schema, seed, runs and expected outputs ([its README](tests/fixtures/README.md)). |
| `tests/ir-versions` | One IR file from each version of the format, which the reader must read (the current one) or refuse ([its README](tests/ir-versions/README.md)). |
| `tests/emit-check` | A small npm package that type-checks the generated Zod. |
| `tests/emit-lint` | A small npm package that lints the generated Zod. |
| `examples` | Small applications that use what haruspex generates, compiled by the tests and in CI ([greenhouse](examples/greenhouse/README.md)). |
| `tests/prose` | The prose checks (`check.sh`), their npm package and the fixtures that test them. |

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
| `typescript` (npm) | Apache-2.0 | emit-check, examples | Compiles the generated Zod, to prove it type-checks. |
| `zod` (npm) | MIT | emit-check, examples | The library the generated code targets, pinned so the check is repeatable. |
| `eslint`, `@eslint/js` (npm) | MIT | emit-lint | Lints the generated Zod with ESLint's recommended rules, as a team that lints its own code would. |
| `typescript-eslint` (npm) | MIT | emit-lint | The recommended rules for TypeScript, and the parser ESLint needs to read it. |
| `typescript` 6 (npm) | Apache-2.0 | emit-lint | typescript-eslint's parser, which needs the compiler's JavaScript API (below 6.1). |
| `cspell` (npm) | MIT | prose check | Spelling in British English across Markdown, C# and the workflows, identifiers included. |
| `mermaid` (npm) | MIT | prose check | Mermaid's own parser, to prove every diagram in the Markdown parses. |
| `jsdom` (npm) | MIT | prose check | The DOM that Mermaid's parser needs in Node for some diagram types, instead of a browser. |
| Vale (Docker image `jdkato/vale`) | MIT | prose check | Checks the house style's rules in the Markdown and the C# comments. |
| `Microsoft.Data.SqlClient` | MIT | differential tests | Microsoft's SQL Server driver for .NET: the tests must run procedures and read their results as SQL Server returns them. |
| `Testcontainers.MsSql` | MIT | differential tests | Starts a throwaway SQL Server in Docker when no server is configured, as in CI; it can later run a matrix of SQL Server versions. |
| `System.Security.Cryptography.Pkcs` | MIT | differential tests | Not used directly: pinned to a patched version because the one SqlClient brings has two advisories (see `Directory.Packages.props`). |

These three are used only by the differential tests, and nothing in them is shipped. Their transitive
packages are MIT, with one exception: `Microsoft.Data.SqlClient.SNI.runtime`, SqlClient's native network
layer for Windows, is under the Microsoft Software License Terms. They allow it to be used freely "to develop
and test your applications", which is all the tests do; like DacFx's, they ask anyone who *redistributes* it to
indemnify Microsoft. So it must never reach anything haruspex ships: the shipped projects under `src/` must
not reference SqlClient.

The prose check's packages run only in development and CI, and nothing in them is shipped. Most of the 259 npm
packages they bring are MIT, ISC, BSD or Apache-2.0; the others are permissive too (MIT-0, CC0, Unlicense,
BlueOak, WTFPL, MPL-2.0 or Apache-2.0 for DOMPurify), except two that are not ordinary code licences: cspell's
dictionary of common misspellings is CC BY-SA 4.0, and `elkjs`, a layout engine Mermaid includes but the check
never calls, is EPL-2.0. Both would matter only to someone redistributing them, which this repository does not.

The corpus in `tests/corpus` is data, not a dependency: Microsoft's sample databases under the MIT licence, whose
`license.txt` is kept beside them. Nothing builds or ships it.

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
