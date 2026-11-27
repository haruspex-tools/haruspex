# The documentation

What each page is for, and where to start for what you want to do. The [project's README](../README.md) says what haruspex
is, what it does today and what it can't see; read it first whichever path you take.

## Reading paths

### Essentials: to use haruspex today

What every project needs to get typed results. Read in order, these give a working setup.

1. [Five minutes with AdventureWorks](quick-start.md): from a fresh clone to the schemas and the function for one procedure,
   and a script that checks parameters and rows with them.
2. [The database module](database-module.md), up to its advanced features: how an application calls a procedure through its
   generated function, what comes back, how parameters are checked, the three ways a call fails, where the functions run,
   and the functions of query files.
3. [The application's own types](layers.md): where the generated types end and the application's begin, with a thin step
   written by hand on the generated type.
4. [The example application](../examples/greenhouse/README.md): a small TypeScript application that calls its procedures
   through a generated module, as a team would commit it.
5. [The configuration reference](configuration.md), up to its advanced settings: `haruspex.json`'s `source` (a folder, a
   DACPAC or a project), `schemas`, `queries`, `emit` and `check`, and "The settings you probably want", which says what most
   projects add next.
6. [The command line](command-line.md), up to its advanced features, for `init`, `generate` and `check`: what each prints,
   their options, the environment variables they read and their exit codes; [the CLI's README](../src/Haruspex.Cli/README.md)
   for what a run guarantees; and [the diagnostics](diagnostics/README.md), when it reports a problem: each code has a page
   saying what it means and what to do.
7. [What haruspex models](coverage.md), to see before you start whether your procedures use something it reads as unknown.

### Advanced features

What some projects need. Each line starts with the situation it is for, so you can tell whether you need it; the page it
links to is the one place that describes it.

- **The application and the database deploy separately:** [a baseline](configuration.md#the-baseline-baseline), so that the
  types [work against both](command-line.md#generating-against-a-baseline) and `check`
  [fails a change](command-line.md#checking-against-a-baseline) the running application would fail on.
- **The callers' connections set `ANSI_WARNINGS` or `ARITHABORT`, or hold `UNMASK`:** declare
  [the callers' session](configuration.md#the-callers-session-session), and haruspex claims more.
- **A procedure's results can't be known from the project**, such as one that runs dynamic SQL:
  [declared results](configuration.md#declared-results-results).
- **A column stores JSON:** [declare its JSON Schema](configuration.md#json-stored-in-a-column-columns), and see
  [what a declaration reads](configuration.md#the-json-schemas-a-declaration-reads).
- **The database should carry its JSON columns' schemas**, in its scripts and its DACPAC:
  [name a column's schema in the SQL](configuration.md#naming-a-columns-schema-in-the-sql).
- **The team knows what the database can't prove**, or wants other names for a procedure's types and result sets:
  [overrides](configuration.md#overrides), and [naming](configuration.md#naming) for every name.
- **The database keeps descriptions under a name of its own:** [descriptions](configuration.md#descriptions-descriptions).
- **The source is a DACPAC built by MSBuild.Sdk.SqlProj**, or another tool that isn't DacFx's build task:
  [`builds`](configuration.md#a-package-built-by-another-tool-builds).
- **The source is a folder of scripts whose tables list their values in a `CHECK`:**
  [the database's collation](configuration.md#the-databases-collation-collation).
- **The database is kept as folders side by side** with folders that aren't its own:
  [several source folders](configuration.md#several-folders-source-as-a-list).
- **The module is large** and code should import only the part it uses:
  [splitting it into modules](configuration.md#splitting-the-output-into-modules-split).
- **The repository holds several modules**, such as one for each plugin: [`--all`](../src/Haruspex.Cli/README.md), which
  generates or checks them together.
- **A warning the team has read and lives with:** [acknowledge it](configuration.md#warnings-acknowledged-acknowledge).
- **The application deploys first, or with the database**, and an unexpected key should fail the call:
  [the results policy](configuration.md#results-policy-objects).
- **More or fewer comments in the generated code:** [`comments`](configuration.md#comments-comments).
- **A field's type surprises you:** [`explain`](command-line.md#explain) shows its derivation from the committed IR.
- **A tool or a CI system reads the report:** [the formats](command-line.md#formats), JSON, SARIF and GitHub annotations.
- **A procedure takes a table-valued parameter:** [table-valued parameters](database-module.md#table-valued-parameters).
- **A test runs a function without a database:** [a test double](database-module.md#testing-with-a-double).
- **The application calls the driver itself:** [the schemas alone](database-module.md#calling-the-driver-yourself), or
  `"json-schema"` in [`emit`](configuration.md#what-is-written-emit) for another language.
- **The application's step should account for every key of a row:**
  [the mapping helper](layers.md#a-mapper-that-accounts-for-every-key).

### To decide whether to trust it

1. The README's [guarantees and limits](../README.md#what-it-guarantees-and-what-it-cant-see): the one rule, what the
   source can't show, what haruspex assumes, and the larger gaps known today.
2. The guide's [first chapter](internals/01-the-shape-of-the-problem.md), for why haruspex infers from the source rather
   than asking the server, and why it says unknown rather than guess.
3. [Observing the engine](internals/09-observing-the-engine.md): how every rule haruspex relies on is checked against a
   real SQL Server, and what that can't show.
4. [The corpus figures](corpus-figures.md) and [the harvest](corpus-harvest.md): how much of public databases it types,
   measured by a test, and what they show it missing.
5. [The decision log](decisions/README.md), for any choice that looks odd: each record gives the alternatives and why they
   lost.
6. [Support](../SUPPORT.md) and [security](../SECURITY.md): there is no support commitment, and the tool reads source files
   only.

### To contribute

1. [Contributing](../CONTRIBUTING.md): the principle every change keeps, test first, and how to build and test, the
   differential tests against SQL Server included.
2. [The guide](internals/README.md): how haruspex works and why, read in order, starting from its "Start here".
3. [The architecture](architecture.md): the layers, what each may know, and the README of each project.
4. [The house style](style.md), for anything a reader reads, and [the agent conventions](../AGENTS.md) for coding agents.

## Words the pages use

A few words have a fixed meaning everywhere in the documentation.

| Word | Meaning |
|---|---|
| claim | Something haruspex states about a value, such as "never null" or "at most 50 characters", which must hold for every execution. |
| unknown | What haruspex says when it can't prove a shape, always with a reason; never a guess. The generated code accepts any value there. |
| shape | What a result, field or value looks like: its type, whether it may be null or absent, and its facets. |
| facet | A fact about a value beyond its type: a length, a range, a number of decimal places, or a value set. |
| value set | The closed set of values a string or number can take, from a `CHECK` constraint. It is generated as an enum only where the value that arrives is proven to be exactly one of them, and as a note otherwise. |
| JSON text, live JSON | A string that holds JSON, such as a JSON column, or a `FOR JSON` subquery's output in a column of a row; and JSON that a document embeds as JSON rather than as a string, such as a `FOR JSON` subquery inside `FOR JSON`, or `JSON_QUERY`. |
| IR | The intermediate representation, `haruspex.ir.json`: every claim and unknown, with its reasons, from which the Zod is generated. |
| override, declaration | What a person writes in `haruspex.json` to correct or complete what was inferred: a name, a claim about a value, the application's own type for a value, or a procedure's results. A value declared never null, a value set declared exact, or declared results need a reason (`why`); the generated code still checks every one. |
| observed | Established by running SQL Server in a test, not by reading its documentation. |
| ScriptDom | Microsoft's parser for T-SQL, the one SQL library haruspex uses; [What haruspex models](coverage.md) lists its constructs by its names. |
| fixture, corpus | A database the tests use to check one thing, invented except for two that copy tables from Microsoft's samples to run the README's examples; and a public database, written by others, used to measure coverage. |

## Every page

| Page | What it is for |
|---|---|
| [README](../README.md) | What haruspex is, what an application gets from it, examples on Microsoft's samples (two procedures and a query file), its advanced features in a list, what it guarantees, its status and roadmap, how to install and run it, and who it is not for. |
| [Five minutes with AdventureWorks](quick-start.md) | A first run, from a fresh clone, that a test runs as written. |
| [The database module](database-module.md) | The generated functions: calling a procedure, what comes back, the parameters' checks, the three failures and where they run; and reading results with the schemas alone. |
| [The application's own types](layers.md) | The three layers: the generated function calls, the generated schema checks, and a thin step written by hand makes the application's types; and the mapping helper, with which that step accounts for every key of a row. |
| [The command line](command-line.md) | `init`, `generate`, `check` and `explain`: what each prints, their options, the formats for tools and CI, the environment variables they read and their exit codes. How a run works inside is [the CLI's README](../src/Haruspex.Cli/README.md). |
| [The generated code](../src/Haruspex.Emit.Zod/README.md) | What the Zod module holds, and how each SQL type arrives: in JSON, and in a row as the mssql driver delivers it. |
| [Configuration](configuration.md) | Every setting of `haruspex.json`, the essentials first and then the advanced settings, held to its [JSON Schema](../schemas/haruspex.schema.json) by a test. |
| [Diagnostics](diagnostics/README.md) | Every code haruspex reports, with a page each: what it means, why, and what to do. |
| [Why a type is what it is](reasons/README.md) | Every reason the IR gives for a type, a null rule, an unknown reason or a cardinality, with a page each: what it means, and the test it rests on. |
| [What haruspex models](coverage.md) | Every T-SQL construct and built-in function, and what haruspex does with each; written by a test. |
| [Corpus figures](corpus-figures.md) | How much of Microsoft's two vendored samples haruspex types; written by a test. |
| [The harvest](corpus-harvest.md) | The same figures for more public corpora, and what they show haruspex missing, ranked; written by a test. |
| [The guide](internals/README.md) | How haruspex works and why a static analyser is built this way, in twelve chapters. |
| [Architecture](architecture.md) | The layers, one project each, and what each may know; held to the code by a test. |
| [Decisions](decisions/README.md) | Each design decision, with its context, the alternatives and why they lost. |
| [The IR's format](../src/Haruspex.Ir/README.md) | `haruspex.ir.json`, the record of what was inferred and why, with its [JSON Schema](../schemas/ir/README.md). |
| [The example application](../examples/greenhouse/README.md) | A TypeScript application calling its procedures through a generated module, compiled in CI. |
| [Contributing](../CONTRIBUTING.md) | How to build, test and change haruspex, and the rules every change keeps. |
| [Agent conventions](../AGENTS.md) | The extra rules for coding agents working in the repository. |
| [House style](style.md) | How everything a reader reads is written. |
| [Support](../SUPPORT.md) | There is no support commitment; where to take a question or a bug. |
| [Security](../SECURITY.md) | How to report a vulnerability privately, and what haruspex reads and writes. |
| [Licence](../LICENSE) | Apache 2.0. |

The tests have their own READMEs, for contributors: [the fixtures](../tests/fixtures/README.md), [the differential
tests](../tests/Haruspex.DifferentialTests/README.md), [the corpora](../tests/corpus/README.md), [the coverage
page](../tests/coverage/README.md) and [the prose checks](../tests/prose/README.md).
