# The corpora

Fixtures say whether haruspex gets a construct right. They don't say how much of a real database it can type, because
someone chose each fixture to test one thing. A corpus is the other half: a body of code written by other people for their
own reasons, run through haruspex unchanged, to measure coverage. The corpora come in two kinds:

- **Vendored**: two of Microsoft's sample databases, copied into this folder, so that they are read on every build with no
  network. The guide's worked examples and the project's README rest on them. [The corpus figures](../../docs/corpus-figures.md)
  are what haruspex makes of them.
- **Fetched**: every other public corpus, listed in `fetched.json` and fetched by `fetch.mjs` into a folder outside the
  checkout. Nothing from them is committed but counts: [the harvest](../../docs/corpus-harvest.md), which ranks what every
  corpus shows haruspex missing, and gives each fetched corpus's figures.

Both pages are written by tests, so neither can drift from the code
([decision 0020](../../docs/decisions/0020-public-figures-come-from-public-corpora.md)).

## The vendored corpora

### What they are

Both are Microsoft's sample databases for SQL Server, from
[microsoft/sql-server-samples](https://github.com/microsoft/sql-server-samples), under its MIT licence
(`sql-server-samples/license.txt`, kept beside them).

- **WideWorldImporters**, as an SSDT database project: `WideWorldImporters.sqlproj` and the 336 scripts beside it. It is
  the newer sample, written to show SQL Server 2016's features: temporal tables, JSON, in-memory tables, row-level security.
- **AdventureWorks**, as one install script, `instawdb.sql`, written for sqlcmd: it creates the database and every object
  in `GO` batches, then loads data from CSV files. The CSV files (about 100 MB) are not vendored; a static analysis doesn't
  read data.

### Why these two

The figures in public text come only from public code, so that anyone can reproduce them
([decision 0020](../../docs/decisions/0020-public-figures-come-from-public-corpora.md)). These two are the samples a SQL Server
developer is most likely to know, and they come in two common forms of a database's source: a project of one file per
object, and a single script. Each also shows what haruspex can't read yet, which the figures page states rather than
hides.

### How they are kept

The files are **vendored**: copied into the repository, byte for byte as they are at one commit upstream, so the tests
need no network and always measure the same input. `manifest.json` records the repository, the commit's full SHA, the
licence, and every file's path and SHA-256. The files keep their upstream paths below `sql-server-samples/`, so a file's
origin is its path.

Two tests hold this to the manifest (`CorpusManifestTests`): every file matches its hash, and no file is there that the
manifest doesn't list. The vendored files are someone else's work, so no tool here may change or judge them: git doesn't
convert their line endings (`.gitattributes`), and the spelling, style and formatting checks leave them out (each
configuration says so where it does). Nothing builds them: the `.sqlproj` is data, and the solution names no project here.

### How the figures are made

`CorpusFiguresTests` reads each corpus with Sources, infers every procedure in every schema, counts the result as
`docs/corpus-figures.md` defines, and compares that page with what it would write. When haruspex changes what it types,
the test fails, printing the difference. If the change is meant, rewrite the page and read the diff:

```sh
HARUSPEX_ACCEPT=1 dotnet test --filter CorpusFiguresTests
git diff docs/corpus-figures.md
```

CI never rewrites it. A figure quoted elsewhere is copied from the page, and a test checks the copy: the style guide's
example is AdventureWorks' figure.

### How to refresh them

To move to a newer commit upstream, run the refresh script with the commit, a branch or a tag (it needs Node 18 or later):

```sh
node tests/corpus/refresh.mjs master
```

It resolves the name to a full SHA, downloads each selected file from that commit, checks each against the commit's own
hash of it, and only then rewrites `sql-server-samples/` and `manifest.json`. What it selects is listed at the top of the
script, with why. Then rewrite the figures as above, and commit the three together, so the diff shows what changed upstream
and what that did to the figures.

## The fetched corpora

Fetching is the default for a corpus: a contributor adds one with an entry in `fetched.json` and the regenerated harvest,
and the repository holds none of its code, so it redistributes nobody's work and needs no review of what vendoring a
licence would ask. That is also why a corpus under a copyleft licence, such as Bitwarden's server under the AGPL, can be
measured: what is committed is counts, never its code, names, IR or generated Zod. [CONTRIBUTING](../../CONTRIBUTING.md#adding-a-corpus)
says how to add one and what its licence must allow.

### Fetching them

```sh
node tests/corpus/fetch.mjs
```

It needs Node 18 or later and git, and fetches each corpus that isn't already there into `~/.cache/haruspex/corpora/<id>`, or
below `HARUSPEX_CORPORA` when that is set; the tests look in the same place. Give ids to fetch only those. A corpus costs one
commit's worth of the paths it takes, without history: git fetches the commit with `--depth 1` and no file contents, then a
sparse checkout brings only those paths, so even a repository of hundreds of megabytes costs a few. Every file is checked
against its SHA-256 in the manifest before anything is put in place, so a fetch that fails leaves what was there. The fetched
code is data: nothing here runs its scripts or builds it.

### The tests

`CorpusHarvestTests` reads every corpus and compares `docs/corpus-harvest.md` with what it would write, as `CorpusFiguresTests`
does for the vendored ones; rewrite it with `HARUSPEX_ACCEPT=1` in the same way. A test that needs a fetched corpus checks
its folder against the manifest first, and fails if a file differs, so a stale fetch can't measure another commit than the
one the page names. When a corpus hasn't been fetched, the test is skipped with a message saying how to fetch it.
`HARUSPEX_REQUIRE_CORPORA=1` turns that skip into a failure; CI sets it, after fetching the corpora into a cache keyed on
the manifest, so the page is always checked there. `FetchedCorpusManifestTests` checks the manifest itself on every build,
fetched or not.

### The manifest

`fetched.json` holds one entry per corpus, in the order the harvest shows them:

| Field | What it is |
|---|---|
| `id` | A short name: the corpus's folder in the cache, and what `fetch.mjs` is given. |
| `name` | Its name on the harvest, as its authors give it. |
| `kind` | `coverage` for an application's database, measured by how much is typed; `tool` for code written to administer a server or test code in it (tools for database administrators, a unit-testing framework), measured by what is read and why what isn't typed is unknown. |
| `repository`, `commit`, `committed` | The upstream repository, the full SHA fetched, and that commit's date. |
| `licence` | Its SPDX identifier, the licence file, and any notices the licence asks to keep with the code (`notices`), all fetched with it. |
| `take` | The upstream paths fetched: a file, or a folder ending in `/`, whose `.sql` and `.sqlproj` files are taken. |
| `read` | How it is read: a list of databases, each a `project` (a `.sqlproj`, read as `haruspex generate` reads one) or a `folder` (its scripts read as a folder's, less those left out). A corpus of several small databases, such as the JSON demos, reads each on its own and sums the figures. |
| `leftOut` | Scripts fetched but not read, in groups, each with the reason the harvest gives. |
| `about` | What it is and what it exercises, for the harvest. |
| `files` | Every file fetched, with its length and SHA-256, written by `fetch.mjs`. |

To move a corpus to another commit, or to fill in a new entry's `commit`, `committed` and `files`, pin it to a commit, a branch
or a tag:

```sh
node tests/corpus/fetch.mjs pin <id> <ref>
```

It fetches that commit, rewrites the entry, and puts the files in the cache. Then rewrite the harvest, read both diffs, and
commit them together.
