# The documentation

What each page is for, and where to start for what you want to do. The [project's README](../README.md) says what haruspex
is, what it does today and what it can't see; read it first whichever path you take.

## Reading paths

### To use haruspex today

1. [Five minutes with AdventureWorks](quick-start.md): from a fresh clone to a generated schema and a script that parses
   rows with it.
2. [The example application](../examples/greenhouse/README.md): a small TypeScript application that imports a generated
   module, as a team would commit it.
3. [The configuration reference](configuration.md): `haruspex.json`, which says which procedures to type, how to name the
   generated code, and how to correct or declare what can't be inferred.
4. [The command line](../src/Haruspex.Cli/README.md), for `generate`'s options, what it prints and its exit codes; and
   [the diagnostics](diagnostics/README.md), when it reports a problem: each code has a page saying what it means and what
   to do.
5. [What haruspex models](coverage.md), to see before you start whether your procedures use something it reads as unknown.

### To decide whether to trust it

1. The README's [guarantees and limits](../README.md#what-it-guarantees-and-what-it-cant-see): the one rule, and what
   the source can't show.
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
| JSON text, live JSON | A string that holds JSON, as `FOR JSON` returns it in a row; and JSON that a document embeds as JSON rather than as a string, such as a `FOR JSON` subquery or `JSON_QUERY`. |
| IR | The intermediate representation, `haruspex.ir.json`: every claim and unknown, with its reasons, from which the Zod is generated. |
| override, declaration | A claim a person makes in `haruspex.json`, about a value or a procedure's results, with a required reason; the generated code still checks it. |
| observed | Established by running SQL Server in a test, not by reading its documentation. |
| fixture, corpus | An invented database the tests use to check one thing; and a public database, written by others, used to measure coverage. |

## Every page

| Page | What it is for |
|---|---|
| [README](../README.md) | What haruspex is, two examples on Microsoft's samples, what it guarantees, how to install and run it, and who it is not for. |
| [Five minutes with AdventureWorks](quick-start.md) | A first run, from a fresh clone, that a test runs as written. |
| [The command line](../src/Haruspex.Cli/README.md) | `haruspex generate`: its arguments and options, what it prints, and its exit codes. |
| [Configuration](configuration.md) | Every setting of `haruspex.json`, held to its [JSON Schema](../schemas/haruspex.schema.json) by a test. |
| [Diagnostics](diagnostics/README.md) | Every code haruspex reports, with a page each: what it means, why, and what to do. |
| [What haruspex models](coverage.md) | Every T-SQL construct and built-in function, and what haruspex does with each; written by a test. |
| [Corpus figures](corpus-figures.md) | How much of Microsoft's two vendored samples haruspex types; written by a test. |
| [The harvest](corpus-harvest.md) | The same figures for more public corpora, and what they show haruspex missing, ranked; written by a test. |
| [The guide](internals/README.md) | How haruspex works and why a static analyser is built this way, in twelve chapters. |
| [Architecture](architecture.md) | The layers, one project each, and what each may know; held to the code by a test. |
| [Decisions](decisions/README.md) | Each design decision, with its context, the alternatives and why they lost. |
| [The IR's format](../src/Haruspex.Ir/README.md) | `haruspex.ir.json`, the record of what was inferred and why, with its [JSON Schema](../schemas/haruspex.ir.schema.json). |
| [The example application](../examples/greenhouse/README.md) | A TypeScript application using a generated module, compiled in CI. |
| [Contributing](../CONTRIBUTING.md) | How to build, test and change haruspex, and the rules every change keeps. |
| [Agent conventions](../AGENTS.md) | The extra rules for coding agents working in the repository. |
| [House style](style.md) | How everything a reader reads is written. |
| [Support](../SUPPORT.md) | There is no support commitment; where to take a question or a bug. |
| [Security](../SECURITY.md) | How to report a vulnerability privately, and what haruspex reads and writes. |
| [Licence](../LICENSE) | Apache 2.0. |

The tests have their own READMEs, for contributors: [the fixtures](../tests/fixtures/README.md), [the differential
tests](../tests/Haruspex.DifferentialTests/README.md), [the corpora](../tests/corpus/README.md), [the coverage
page](../tests/coverage/README.md) and [the prose checks](../tests/prose/README.md).
