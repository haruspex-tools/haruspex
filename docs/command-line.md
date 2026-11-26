# The command line

The command `haruspex` has four subcommands. `init` writes a starter config file, `generate` writes the module and says
what changed since the last run, `check` fails in CI when the module in the output folder (in CI, the committed one) would change,
and `explain` says why a field is what it is. The command is run many
times a day, so by default it says what it did and what needs attention, and counts the rest. Everything else is a flag away.
This page says what each prints, the options, the machine formats, the environment variables it reads and its exit codes. How
a run works inside is [the CLI's README](../src/Haruspex.Cli/README.md); why the output is designed so is
[decision 0064](decisions/0064-the-command-line-s-output-is-designed.md).

```sh
haruspex init                                           # find the database below here, write haruspex.json
haruspex generate                                       # write the module, and say what changed
haruspex generate db --include 'sales.*' --out src/db   # without a config file
haruspex check --all                                    # in CI: fail if a module in its output folder would change
haruspex generate --baseline release/Shop.dacpac        # types that work before and after the change is deployed
haruspex check --baseline release/Shop.dacpac           # and fail if the application already running would break
haruspex explain shop.order_summary last_shipped        # why a field is what it is, from the committed IR
```

## What a run prints

A run that succeeds prints its summary to standard output first: what it typed, what it wrote, and what changed since the
last run. Errors and warnings follow in full on standard error, each as a compiler writes one
(`file:line:column: severity code: message`), so editors and terminals go to it. Notes, one for each unknown, are counted by
reason, not listed. Paths below the working directory are shown from it.

**A first run** has nothing to compare with, so it says so and counts the unknowns by reason. Each reason's code has a page
in [the diagnostics](diagnostics/README.md):

```text
haruspex: 4 procedures, 3 unknowns. Wrote src/db/haruspex.ir.json and src/db/haruspex.zod.ts.
First output in src/db, so nothing to compare with.
Unknowns by reason (--verbose lists each):
  1  HAR2009  A column that can't be found or is ambiguous
  1  HAR2013  JSON whose shape isn't visible
  1  results only a declaration can give, as the warnings say
What each code means: https://haruspex.tools/diagnostics/
haruspex.json: warning HAR4204: kennel.find_dogs: its result sets are unknown: EXEC sp_executesql @sql runs dynamic SQL, …
```

Output split into modules ([`split`](configuration.md#splitting-the-output-into-modules-split)) is a file per emitter per
module, so the first line counts them and names the modules instead:

```text
haruspex: 6 procedures, 2 query files, no unknowns. Wrote 15 files to src/db: the IR and the modules admin, queries, sales and shop, with what they share.
```

The first line doesn't count the list of the files written, `haruspex.files.json`, which `generate` keeps beside them. A run that
no longer writes a file the last one did, for a module taken out of the split or an emitter taken off `emit`, removes it, as the
list names it, and says so on the next line
([the files generate removes](configuration.md#the-files-generate-removes)):

```text
Removed src/db/admin.jsonschema.json, src/db/admin.mssql.ts and src/db/admin.zod.ts, which this run no longer writes.
```

**A run that changes nothing**, which is most runs, is one line:

```text
haruspex: nothing changed since the last run: 4 procedures, 3 unknowns (--verbose lists them).
```

**A run after a change to the database** reads the IR it is about to write over and says what changed, as `check` would:
each change of claim, marked `+` for what a reader now receives, `-` for what it no longer does and `~` for the rest; then
the unknowns that are new or resolved, matched by their object, path and reason, so a procedure moved down its file keeps its
unknowns:

```text
haruspex: 4 procedures, 3 unknowns. Wrote src/db/haruspex.ir.json and src/db/haruspex.zod.ts.
7 changes since the last run:
  - kennel.count_dogs: removed
  - kennel.get_dog results[0][].nickname: removed (unknown (unresolved-column), nullable)
  + kennel.get_dog results[0][].notes: added (string, nvarchar(max), nullable)
  ~ kennel.get_dog results[0][].name: became nullable
  ~ kennel.list_dogs results[0][].name: became nullable
  ~ kennel.list_dogs results[0][].name: presence changed from always to omittedWhenNull
  + kennel.walk_dogs: added
Unknowns: 1 new, 1 resolved, 2 as before (--verbose lists them).
  + kennel.walk_dogs results[0][].walker: HAR2008 A table the project doesn't define (db/procedures.sql:12:8)
  - kennel.get_dog results[0][].nickname: HAR2009 A column that can't be found or is ambiguous
What each code means: https://haruspex.tools/diagnostics/
```

When no claim changed but an emitted file's text did (a comment written from a reason, or the output of another version), it
names the file. An earlier IR of another version, or one that doesn't read, can't be compared: the run says why and gives the
first run's summary.

**A run that fails** writes nothing, gives each error in full, and closes with a line saying so:

```text
db/broken.sql:1:42: error HAR1001: Incorrect syntax near 'FROM'.
haruspex: 1 error; nothing was written.
```

**`check`** says the same, but writes nothing and compares with the files in the output folder: in CI, the ones committed. When it passes it is one line:
`haruspex: 3 procedures, 2 unknowns (--verbose lists them). src/db is up to date.` When it fails, each change is an error at
its procedure (HAR6014), or, for a value that became nullable, at its new reason's place (the column declared nullable, the
outer join), and the closing line says what to do. A check that fails on unknowns
(`"check": { "unknowns": "fail" }`) lists them without being asked.

**`--verbose`** adds every note to the default, each unknown grouped under the procedure or query file it is in:

```text
kennel.get_dog (1 unknown)
db/procedures.sql:5:26: note HAR2009: kennel.get_dog results[0][].nickname: The table kennel.dog has no column nickname.
```

**`--quiet`** prints errors only, and nothing at all when the run succeeds.

**Warnings acknowledged.** A warning the config file's `acknowledge` names, by its code and subject, is counted rather than
said (`2 warnings acknowledged (--verbose lists them).`); `--verbose` lists each with why
([the configuration](configuration.md#warnings-acknowledged-acknowledge)).

## Options

`generate` and `check` take the same options, and `check` one more:

| Argument or option | What it is |
|---|---|
| `source` | The folder of `.sql` files, the `.sqlproj` or the `.dacpac` to read; or several folders of `.sql` files, read as one database (`haruspex generate tables procedures`), none of them a `.sqlproj` or a `.dacpac` ([HAR6044](diagnostics/HAR6044.md)). Replaces `source` in the config file ([several folders](configuration.md#several-folders-source-as-a-list)). |
| `--config`, `-c` | The config file. By default, `haruspex.json` in the working directory or the nearest folder above it. Give it again for more modules. |
| `--all`, `-a` | Every `haruspex.json` below the folder given, or below the working directory, each a module. |
| `--include`, `-i` | Procedures to type, as `schema.name`, where `*` and `?` match within a part; give it again for more. Replaces the config file's includes for this run, which then removes no file of the last run's ([the files generate removes](configuration.md#the-files-generate-removes)). |
| `--out`, `-o` | The folder to write to, or for `check` to compare with. Replaces `out` in the config file. |
| `--baseline`, `-b` | The database before the change being made, a folder of `.sql` files, a `.sqlproj`, a `.dacpac` or an IR file: `generate` writes types that work against both ([below](#generating-against-a-baseline)), and `check` also fails on what the application already running would fail on ([below](#checking-against-a-baseline)). Replaces `baseline` in the config file. |
| `--fit-only` | `check` only, with a baseline: judge only whether the module fits it, without reading the output folder ([below](#checking-against-a-baseline)). |
| `--verbose`, `-v` | Also list every note. |
| `--quiet`, `-q` | Print errors only. |
| `--color` | `auto`, the default, `always` or `never` (below). |
| `--format` | `text`, the default; `json`, `sarif` or `github` (below). |

`init` takes one argument, the `.sqlproj`, `.dacpac` or folder of `.sql` files to read, when it shouldn't look for one.
`haruspex --help` and `haruspex <command> --help` list the same, and `haruspex --version` prints the version.

## Generating against a baseline

An application and its database deploy separately, so a new release of the application may meet the database before its
latest change or after it. With a baseline, the database before, `generate` writes types that work against both: each result
accepts what either database returns, and each function sends only what both procedures take. The types loosen while the two
differ and tighten again when the baseline moves on.

```text
$ haruspex generate --baseline release/database
db/procedures.sql:1:1: warning HAR6034: shop.get_order results[0][]: customerName went and customer_name appeared; if it was renamed, read both until the baseline moves on. The combined type has both keys, each optional.
db/procedures.sql:3:1: error HAR6033: shop.place_order parameters["@channel"]: gained the required parameter @channel, which the baseline's procedure doesn't take: give it a default first, or ship the change in two releases. Until then the module leaves out its function and types, since none works against both databases.
haruspex: 1 change no one function bridges, each procedure left out of the module written. Ship each in two releases.
```

The types are merged rather than given as alternatives: one object type, where a key only one database returns may be left out
and a value is nullable if either's is (the rules are in [the configuration](configuration.md#the-baseline-baseline)). A change no
one function can bridge is an error at its procedure (HAR6033), which the module leaves out, while it is still written for every
other procedure; the run then fails. A key gone with another appeared is said as a possible rename (HAR6034), a procedure only
one database has is left out (HAR6035), and a function whose return shape changes is a warning (HAR6028). The IR written is
the combined one, whose `source` names the baseline, and each module's header says it is combined. Without a baseline,
`generate` writes what it always did.

`check` with the same baseline compares the output folder with the combined module, as `generate` with it writes it, so a
config file that names the baseline gives both commands the same one. The baseline is the database itself: its source (the
release's `.dacpac`, or its tag checked out into a folder) or an IR file written without a baseline. A combined IR describes
an application, so it is a baseline for `check --fit-only` only (HAR6036).

## Checking against a baseline

An application and its database deploy separately, so for a while the application already running meets the database being
deployed. With a baseline, the database the running application was built against, `check` also fails on each change that
application would fail on, and warns of each it sets aside:

```text
$ haruspex check --baseline release/database
db/procedures.sql:4:1: error HAR6026: shop.list_orders results[0][].customer: became nullable, and the application's schema refuses null. The application already running, built against the baseline, would fail: ship a change it can take first, and this one once it is redeployed.
db/procedures.sql:9:1: warning HAR6027: shop.get_order results[1]: added, and the application ignores a result set after those it knows. The application already running sets it aside until it is redeployed.
haruspex: 1 change the application already running, built against release/database, would fail on. Ship each in two steps: first a change it can take, then the rest once it is redeployed.
```

Each change is said as `check` says a change of claim, with why the running application fails on it or sets it aside: the
rules, for results and for parameters, are in [the configuration](configuration.md#the-baseline-baseline). A change it would
fail on is shipped in two steps (expand, then contract): first one it can take, such as a new column beside the old, then the
rest once it has been redeployed. A function whose return shape changes, one result becoming `{ result1, result2 }`, is a
warning (HAR6028): the running application keeps its own functions, but each call changes when it is next built.

The baseline is given with `--baseline` or `"baseline"` in the config file, for one module at a time. It is read as a source is,
with the module's config file, or from an IR file this version wrote. The comparison with the output folder is still made,
unless `--fit-only` asks for the fit alone, as the database's side below does. A query file is the application's
own SQL, sent with the text it was built with, so it is judged only against an IR baseline whose text is unchanged; otherwise
the check warns that it isn't (HAR6031). A baseline is never fetched: a step before the check puts it there. Two
recipes for GitHub Actions, one for each side. Each pins its actions by commit, with the version in a comment, as haruspex's
own workflow does, since a tag can be moved.

**The application's side**: the database as of the last release, checked out from its tag into a folder, beside the change.
The same baseline makes the module's types work against both databases, so the module a developer generates with it, and
commits, is the one the check compares with.

```yaml
name: database
on: pull_request
permissions:
  contents: read
jobs:
  check:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
        with:
          fetch-depth: 0             # the tags, to find the last release
          persist-credentials: false
      - uses: actions/setup-node@949feb2413d6458794dcd2491c4babbbce0c15c1 # v7.1.0
        with:
          node-version: lts/*
      - run: npm ci
      - name: The database as last released
        run: git worktree add --detach release "$(git describe --tags --abbrev=0)"
      - run: npx haruspex check --baseline release/database
```

With `"baseline": "release/database"` in `haruspex.json`, the last step is `npx haruspex check`. A release pipeline that keeps
the database's `.dacpac` can download that instead, and name it as the baseline. A developer generates with the same baseline,
checked out the same way (`git worktree add --detach release <tag>`), so the committed module is the combined one; after a
release, the baseline moves on to it, and the types tighten.

**The database's side**: a database with its own repository checks each application that uses it, with the application's
config file and its committed IR as the baseline, and refuses a deploy that would break one of them already running. The
check uses `--fit-only`, so only the fit is judged, not whether the module is current. Each application is
checked out at the commit that is deployed (here its default branch), and its own pinned haruspex is run, so its IR is of the
version that reads it.

```yaml
name: applications
on: pull_request
permissions:
  contents: read
jobs:
  check:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        application: [your-org/shop-web, your-org/warehouse]
    steps:
      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
        with:
          path: database
          persist-credentials: false
      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
        with:
          repository: ${{ matrix.application }}
          path: application
          persist-credentials: false
          # A private repository needs a token that can read it: token: ${{ secrets.APPLICATIONS_TOKEN }}
      - uses: actions/setup-node@949feb2413d6458794dcd2491c4babbbce0c15c1 # v7.1.0
        with:
          node-version: lts/*
      - run: npm ci
        working-directory: application
      - run: npx haruspex check ../database/Shop.sqlproj --baseline src/db/haruspex.ir.json --fit-only
        working-directory: application
```

Here `src/db` is the application's `out`, and `../database/Shop.sqlproj` replaces its `source` for the run. An application
generated with a baseline commits the combined IR, which says what the application accepts against either database, and is
the right baseline here.

## `explain`

`haruspex explain` says why a field is what it is, from the committed IR alone: an application's repository keeps the IR beside
the generated code, and often not the database's SQL ([decision 0078](decisions/0078-the-ir-says-where-each-field-came-from.md)).
With a field, it prints the field's derivation: the table columns it is read from, then each rule that acted on it, each with
its code and where in the SQL it applied. When that SQL is at hand (the config file's source, a query file beside it) and is the
file the IR was written from (its hash is the one the IR records), the line is printed under the reference; a package's line
only when its SHA-256 is the one the IR recorded, since a package keeps no file names and the line is in its module's own text.
Otherwise the reference stands alone, which is no error: an edited file shows no line rather than the wrong one. So the IR
records each script's hash, and an edit that changes no type (a comment, a blank line) still changes the IR: `check` passes,
since it compares claims, and `generate` says "No claim changed since the last run; the IR records the new text of 1 file".
Run `generate` after such an edit to keep `explain`'s lines. A location is
never followed outside the folder it is looked in, and a control character in a line or a name is printed as an escape.

```text
$ haruspex explain shop.order_summary last_shipped
shop.order_summary last_shipped: datetime (local), datetime2(0), nullable
  from shop.order_line.shipped_at (datetime2(0)), declared at tables/order_line.sql:12:5
    | shipped_at  datetime2(0)  NULL,
  null nullable-column at shop.order_line.shipped_at: the column it reads is declared nullable
    | shipped_at  datetime2(0)  NULL,
  null unmatched-row at procedures/order_summary.sql:13:11: it comes from the optional side of an outer join, which has no row when nothing matches
    | LEFT JOIN shop.order_line AS l ON l.order_id = o.order_id
  null empty-aggregate at procedures/order_summary.sql:11:8: it is an aggregate without GROUP BY, which is null over no rows
    | max(l.shipped_at) AS last_shipped
  to change it: if the data rules it out, declare it in the config file with "nullable": false and why
  page nullable-column: https://haruspex.tools/reasons/nullable-column
  …
```

Without a field it lists, a line each, every field of the object whose nullability, presence or type differs from what its
source column declares, and the object's unknowns:

```text
$ haruspex explain shop.order_summary
shop.order_summary: 3 fields differ from what their columns declare.
  line_count    may be null: count-overflow
  total         decimal(38,2), not its column's type; may be null: unmatched-row, empty-aggregate
  last_shipped  may be null: unmatched-row, empty-aggregate
Run haruspex explain shop.order_summary <field> for a field in full.
```

| Argument or option | What it is |
|---|---|
| `object` | The procedure, as `schema.name`, its parts in brackets or not and in any case (`[shop].[Order_Summary]`), two that differ only in case being refused; a query file, by its path; its generated name; or a generated type, by its name (a type a function owns, which several objects reach). |
| `field` | The field's path in the object's results, as the config file's paths are written (`lines[].qty`; the `[]` may be left out). With several result sets, the set first: by its name (`summary.total`) or, when they have none, its place from 1 (`#2.total`). A parameter with its `@` (`@order_id`). Without it, the object's surprises. |
| `--ir` | The IR file. By default, `haruspex.ir.json` in the config file's output folder. |
| `--config`, `-c` | The config file, which says where the IR and the SQL are. By default, found as `generate` finds it. |
| `--json` | One JSON document for tools, below. |

For a field of a combined IR, written with a baseline, it prints each database's side, labelled with its source as the IR
records it (as the config file named it, relative to its folder: `release/Shop.dacpac`, `database`), with its own derivation, and how the two were combined;
a field both give exactly as it is says so. A baseline given as an IR file older than 0.25, or an IR older than 0.25 itself,
says nothing of where its fields came from, and `explain` says so. The codes are the IR's own, each with a page in
[the reasons](reasons/README.md). Asking about something the IR doesn't have is HAR6038, which lists what it has; no IR to read is
HAR6037.

**`--json`** writes one document: `version`, now 1, which a tool checks, and which is provisional until haruspex 1.0;
`derived`, false for an IR older than 0.25; `object`; and `fields`, each with its `path`, the named `type` it is a field of, its `value` in words, its `sql` type when it
has one, `nullable`, `presence`, whether it is a `surprise`, whether its type differs from its columns' (`typeDiffers`), `from`
(each `column`, `sql`, `at`, and `line` when the SQL is at hand), `nullableBecause` (each `rule`, its `text` and `page`, `at`,
`detail` when the reason says what made it so there, such as a JSON path's steps, `line` when at hand, and whether the rule is one a declaration gives, `declared`), `unknown` when its type is one (`code`,
`text`, `page`), and the `explain` command. A field named in full adds the config file's `override` when there is one, `sides`
for a combined field (`baseline` and `candidate`, each with its `source`, or `null` where that database doesn't return the
key, and for the baseline whether it says where its fields came from, `derived`) or `sameInBoth` where both give it alike, and
its `waysOut`. Without a field, `fields`
lists the surprises and `unknowns` the object's (`path`, `reason`, `detail`, `at`); a parameter gives `parameter` (`name`,
`value`, `sql`, `hasDefault`, `output`) instead. The vocabulary is the IR's: its codes, its paths and its locations.

`check` points here: a change of a field's claim ends with the `explain` command for it, once `generate` has written the new
IR, and with a baseline given as an IR file, the command that explains the field there too (`--ir`).

## `init`

`haruspex init` looks below the working directory, four folders deep, for the database: one `.sqlproj` first, then one
`.dacpac` (in `bin` too, where a build writes one, leaving out the packages of SQL Server's system databases a build copies
beside it), then a folder of `.sql` files. Hidden folders, `node_modules`, and `bin` and `obj` for anything but a DACPAC, are
passed over. The folder is the deepest that holds every script a folder source takes something from: a script the folder reader
passes over whole (queries, `INSERT`, `UPDATE`, `DELETE`, `MERGE`, `EXEC`, `SET NOCOUNT`) changes nothing about the database, and
any other (a definition, an `ALTER`, a `NOCHECK`, a bulk load, one that doesn't parse) may, so it is read. So the application's
query files in a folder of their own, beside the database's, are not read as the database's scripts; init names such a folder,
for you to add as [`queries`](configuration.md#query-files-queries) if that is what it holds. A query script in a folder that
holds none of the database's scripts but has one further down, so that the folder can't be named, is named as itself: left out,
with the `queries` entry that would type it, or read as the database's and not typed, with the database's scripts below its
folder, which keep the folder in the source. When the folder that holds every
database script would hold one too, the source is instead [the list](configuration.md#several-folders-source-as-a-list) of the
folders directly below it that hold the database's scripts (`"source": ["procedures", "tables"]`), which leaves the others out.
Where one of those scripts is directly in that folder, which a list of folders can't name, init reads the folder, says it holds
the query files, and names the folders and scripts of the database, for you to put together in one folder of their own. A
folder of query files inside one of the listed folders is read as that folder's, and named with it. A folder source reads every `.sql` file below it
([the configuration](configuration.md#reading-a-folder-of-scripts-source)), so this
keeps the application's own folder, with its installed packages, out of the source. It writes a `haruspex.json` that reads it,
types every procedure of each schema that has one, and writes to `src/db`, or `generated` when there is no `src` folder:

```jsonc
// haruspex's settings, written by haruspex init. Each is described in
// https://haruspex.tools/docs/configuration
// where "The settings you probably want" says which most projects need next: the callers' session,
// a DACPAC's build, the collation of a folder of scripts.
{
  "$schema": "https://haruspex.tools/schemas/main/haruspex.schema.json",
  "source": "database",
  "out": "src/db",
  // Which procedures to type, by schema: every one, to start. Narrow each "include" to those the application calls.
  "schemas": {
    "audit": { "include": ["*"] },
    "sales": { "include": ["*"] },
  },
}
```

It prints what it chose and the next command. When reading the source finds errors (a script that doesn't parse, a project
file that isn't one), it prints them, keeps the file it wrote, and fails (exit code 1). It never writes over a config file (HAR6021). When it finds nothing it says so
(HAR6022), and when it finds two project files, or two DACPACs and no project file, it names them and asks for one (HAR6023):
`haruspex init Sales/Sales.sqlproj`. [The configuration](configuration.md) describes every setting the file can have.

## Formats

`--format` says who reads the report. The detail options shape the text alone: the machine formats hold everything.

| Format | For | What it writes |
|---|---|---|
| `text` | a person | The report above. The default, except in GitHub Actions. |
| `json` | a tool | One JSON document on standard output for the whole run (below). |
| `sarif` | code scanning | A [SARIF 2.1.0](https://docs.oasis-open.org/sarif/sarif/v2.1.0/sarif-v2.1.0.html) log on standard output. |
| `github` | GitHub Actions | The text, then each error and warning as a workflow command, which GitHub shows as an annotation at its line. The default when `GITHUB_ACTIONS` is `true`. |

**JSON.** The document is specified by [`schemas/haruspex.output.schema.json`](../schemas/haruspex.output.schema.json), which
it names in `$schema`: a release at its own tag (`https://haruspex.tools/schemas/v<its version>/...`), any other build on `main`, as
[the configuration](configuration.md) says of the config file's schema. Its `version` is 1: a field may be added without a new version, but one is removed, renamed or given
another meaning only with a new one, so a tool checks it. At the top are the `command`, the run's `exitCode`, `refused` (why
no module ran, when none did) and `modules`, one for each module in the order they ran. Each module has:

| Field | What it is |
|---|---|
| `config` | The config file `--config` named or `--all` found; null when the run looked for one. |
| `out` | The output folder, once settled. |
| `exitCode` | The module's exit code. |
| `typed` | How many `procedures` and `queryFiles` it types; null when the run failed before inferring. |
| `written` | The files `generate` wrote, the IR first. |
| `removed` | The files `generate` removed: those the last run's list of files named that this run didn't write ([the files generate removes](configuration.md#the-files-generate-removes)). Empty for `check`. |
| `unknowns` | Their `count`, and `byReason`: each reason as the IR names it, the code of its note, and how many. |
| `sinceLastRun` | For `generate`: `compared` false with `why`, or true with the `changes` (each with its `kind`, such as `became-nullable`, `object`, `path`, `before`, `after` and the `text` the report prints), `newUnknowns`, `resolvedUnknowns`, `unchangedUnknowns` and `rewritten`. Null for `check`. |
| `acknowledged` | The warnings the config file's `acknowledge` names, each as a diagnostic with its `subject` and `why`. |
| `diagnostics` | Everything else the run found, notes included: each `code`, `severity`, `message`, `file`, `line`, `column` (null where there is none) and `help`, the code's page. |

**SARIF.** One run of the tool `haruspex`, with its version; a rule for each code the run reported, with its title, its page
as `helpUri` and its severity as the default level; and a result for each diagnostic, at its file, line and column. A file is
a URI relative to the repository's root in GitHub Actions (`GITHUB_WORKSPACE`), otherwise to the working directory. Upload it
with GitHub's `upload-sarif` action, or read it with any SARIF viewer.

**GitHub annotations.** Each error and warning becomes `::error` or `::warning`, with its file, line, column and its code and
title as the annotation's title; notes become `::notice` only with `--verbose`, since a module's unknowns would bury its one
error, and GitHub shows only the first few of each kind for a step. Files are named from `GITHUB_WORKSPACE`. Since it is the
default there, a workflow's `haruspex check --all` annotates the pull request with nothing more said.

## Colour, links and progress

Only on a terminal, and only where the terminal is known to show them; output to a file, a pipe or a CI log is plain text,
the same bytes on every machine. It is UTF-8, without a byte order mark, whatever the console's code page on Windows, and
haruspex leaves the console's code page as it was.

- **Colour.** `--color always` or `never` decides. Otherwise `FORCE_COLOR`, set to anything but `0` or `false`, colours, and
  set to one of those doesn't; `NO_COLOR`, set to anything, doesn't, as [its convention](https://no-color.org) asks; `TERM=dumb`
  doesn't; and otherwise a stream is coloured when it is a terminal. Standard output and standard error are decided apart.
- **Links.** On a coloured terminal, each diagnostic code is a hyperlink (OSC 8) to its page. A terminal that doesn't support
  them shows the escape as text, and none says whether it does, so links are made only in one that names itself as one that
  does (Windows Terminal, iTerm2, WezTerm, Visual Studio Code, Ghostty, kitty, Konsole, DomTerm, and those built on VTE 0.50
  or later, such as GNOME Terminal), never in CI. `FORCE_HYPERLINK` decides instead, as colour's variables do. Without links,
  a line under a list of codes points to the index of the pages.
- **Progress.** When a run takes more than half a second, a line on standard error says what it is doing
  (`haruspex: inferring 120 procedures (3s)`), redrawn in place and cleared before the report. Only when standard error is a
  terminal, never when `CI` is set, and not with `--quiet`.

## Environment variables

| Variable | What it does |
|---|---|
| `NO_COLOR` | Set to anything: no colour, unless `--color always` or `FORCE_COLOR` says otherwise. |
| `FORCE_COLOR` | Set to anything but `0` or `false`: colour even off a terminal. `0` or `false`: no colour. |
| `FORCE_HYPERLINK` | Set to anything but `0` or `false`: codes are links whenever there is colour. `0` or `false`: never. |
| `CI` | Set to anything: no progress, and no links unless `FORCE_HYPERLINK` asks. |
| `TERM` | `dumb`: no colour and no progress. |
| `GITHUB_ACTIONS` | `true`: `--format github` by default. |
| `GITHUB_WORKSPACE` | The folder SARIF and annotations name files from. |
| `WT_SESSION`, `TERM_PROGRAM`, `KITTY_WINDOW_ID`, `KONSOLE_VERSION`, `DOMTERM`, `VTE_VERSION` | Read to tell whether the terminal shows links. |

## Exit codes

Fixed, so scripts and CI can rely on them. A run over several modules exits with the highest of theirs.

| Code | Meaning |
|---|---|
| 0 | Success, with or without warnings and notes; `init` wrote its file |
| 1 | The input has errors (SQL that doesn't parse, an object defined twice, a file that isn't a DACPAC), the output couldn't be written, or a check failed (a baseline's included); or `init` wrote its file, but reading the source found errors |
| 2 | The command line or the config file is wrong; or `init` wrote nothing: a config file is already there, or it found no database or more than one |
| 3 | A fault in haruspex itself |
