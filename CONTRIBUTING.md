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

The build treats warnings as errors, with nullable reference types and the recommended analysers on.
Package versions are central (`Directory.Packages.props`) and locked (`packages.lock.json`, committed).
To add or update a package, change `Directory.Packages.props`, run `dotnet restore --force-evaluate`,
and commit the updated lock files with the change. Dependency updates are read by hand, never merged
automatically.

The emit-check test (`tests/emit-check`) writes the Zod generated from the fixtures and compiles it with
`tsc --noEmit` against a pinned `zod`. It runs `npm ci` there the first time. CI sets
`HARUSPEX_REQUIRE_NODE=1`, which turns a missing Node into a failure rather than a skip.

To run the tool from source:

```sh
dotnet run --project src/Haruspex.Cli -- generate tests/fixtures/orders --include 'sales.*' --out out
```

## Layout

| Path | What it holds |
|---|---|
| `src/Haruspex.Ir` | The IR model and its deterministic JSON form. Depends on nothing. |
| `src/Haruspex.SqlServer` | The SQL Server front end: reads `.sql` files with ScriptDom and infers the IR. |
| `src/Haruspex.Emit.Zod` | The Zod emitter. Reads only the IR, never the front end. |
| `src/Haruspex.Cli` | The `haruspex` command. |
| `tests/Haruspex.Tests` | Unit, snapshot and end-to-end tests. |
| `tests/fixtures` | Invented SQL used by the tests. |
| `tests/emit-check` | A small npm package that type-checks the generated Zod. |

## Dependencies

Every dependency is listed here with its licence and the reason it is needed. A new one needs the same, and
the maintainer's agreement.

| Package | Licence | Used by | Why |
|---|---|---|---|
| `Microsoft.SqlServer.TransactSql.ScriptDom` | MIT | front end | Microsoft's own T-SQL parser: the only way to read T-SQL as SQL Server does. |
| `System.CommandLine` | MIT | CLI | Argument parsing, help and usage errors, maintained by the .NET team. |
| `xunit.v3` | Apache-2.0 | tests | The test framework; runs on Microsoft.Testing.Platform (opted into in `global.json`). |
| `typescript` (npm) | Apache-2.0 | emit-check | Compiles the generated Zod, to prove it type-checks. |
| `zod` (npm) | MIT | emit-check | The library the generated code targets, pinned so the check is repeatable. |

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
