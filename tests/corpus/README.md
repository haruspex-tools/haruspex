# The corpora: Microsoft's sample databases

Fixtures say whether haruspex gets a construct right. They don't say how much of a real database it can type, because
someone chose each fixture to test one thing. A corpus is the other half: a body of code written by other people for their
own reasons, run through haruspex unchanged, to measure coverage. This folder holds two, and
[the corpus figures](../../docs/corpus-figures.md) are what haruspex makes of them today, written by a test.

## What they are

Both are Microsoft's sample databases for SQL Server, from
[microsoft/sql-server-samples](https://github.com/microsoft/sql-server-samples), under its MIT licence
(`sql-server-samples/license.txt`, kept beside them).

- **WideWorldImporters**, as an SSDT database project: `WideWorldImporters.sqlproj` and the 336 scripts beside it. It is
  the newer sample, written to show SQL Server 2016's features: temporal tables, JSON, in-memory tables, row-level security.
- **AdventureWorks**, as one install script, `instawdb.sql`, written for sqlcmd: it creates the database and every object
  in `GO` batches, then loads data from CSV files. The CSV files (about 100 MB) are not vendored; a static analysis doesn't
  read data.

## Why these two

The figures in public text come only from public code, so that anyone can reproduce them
([decision 0020](../../docs/decisions/0020-public-figures-come-from-public-corpora.md)). These two are the samples a SQL Server
developer is most likely to know, and they come in two common forms of a database's source: a project of one file per
object, and a single script. Each also shows what haruspex can't read yet, which the figures page states rather than
hides.

## How they are kept

The files are **vendored**: copied into the repository, byte for byte as they are at one commit upstream, so the tests
need no network and always measure the same input. `manifest.json` records the repository, the commit's full SHA, the
licence, and every file's path and SHA-256. The files keep their upstream paths below `sql-server-samples/`, so a file's
origin is its path.

Two tests hold this to the manifest (`CorpusManifestTests`): every file matches its hash, and no file is there that the
manifest doesn't list. The vendored files are someone else's work, so no tool here may change or judge them: git doesn't
convert their line endings (`.gitattributes`), and the spelling, style and formatting checks leave them out (each
configuration says so where it does). Nothing builds them: the `.sqlproj` is data, and the solution names no project here.

## How the figures are made

`CorpusFiguresTests` reads each corpus with Sources, infers every procedure in every schema, counts the result as
`docs/corpus-figures.md` defines, and compares that page with what it would write. When haruspex changes what it types,
the test fails, printing the difference. If the change is meant, rewrite the page and read the diff:

```sh
HARUSPEX_ACCEPT=1 dotnet test --filter CorpusFiguresTests
git diff docs/corpus-figures.md
```

CI never rewrites it. A figure quoted elsewhere is copied from the page, and a test checks the copy: the style guide's
example is AdventureWorks' figure.

## How to refresh them

To move to a newer commit upstream, run the refresh script with the commit, a branch or a tag (it needs Node 18 or later):

```sh
node tests/corpus/refresh.mjs master
```

It resolves the name to a full SHA, downloads each selected file from that commit, checks each against the commit's own
hash of it, and only then rewrites `sql-server-samples/` and `manifest.json`. What it selects is listed at the top of the
script, with why. Then rewrite the figures as above, and commit the three together, so the diff shows what changed upstream
and what that did to the figures.
