# Test DACPACs

The DACPACs the tests read: the fixtures and WideWorldImporters built as projects, two small projects of their own, and a
few built or extracted in other ways. They are checked in, so the tests need neither DacFx nor a network. Nothing in the
solution refers to DacFx; it is used only to make these files, by hand, which its licence allows ("to develop and test
your applications"), and they are rebuilt only when a test needs a new one.

## What is here

| Path | What it is | Built with |
|---|---|---|
| `fixtures/<name>.dacpac` | Each fixture that builds as a project, from its `database/` folder: every one but `checks` | `Microsoft.Build.Sql` 2.3.0 (DacFx 170.5.96), SQL Server 2025's provider |
| `corpus/WideWorldImporters.dacpac` | WideWorldImporters, from the items its vendored project builds, with its collation and compatibility level (130) | the same, with SQL Server 2016's provider: the project names Azure SQL Database's, which refuses the storage its scripts declare |
| `atelier/atelier.dacpac` | `projects/atelier`: what the fixtures don't show, such as an alias type, a table type, a check disabled by `NOCHECK`, a unique index created apart, temporal and graph tables, a synonym, `ANSI_NULLS` and `QUOTED_IDENTIFIER` off by item metadata, an encrypted procedure and view, and a name with `:` and `/` | `Microsoft.Build.Sql` 2.3.0 |
| `harbour/*.dacpac` | `projects/harbour` and the packages it references, as its build leaves them in its `bin` folder: `crane` (the same database), `quay` (referenced by `crane`, so harbour reaches it only through it) and `tally` (another database, named by the SQLCMD variable `$(Tally)`) | `Microsoft.Build.Sql` 2.3.0 |
| `versions/orders-build-sql-<version>.dacpac` | The `orders` fixture under older versions | `Microsoft.Build.Sql` 2.0.0 (DacFx 170.2.70) and 1.0.0 (DacFx 162.5.57), SQL Server 2022's provider |
| `versions/<name>-extract.dacpac` | `atelier` and `orders` deployed to SQL Server 2025 and extracted again | SqlPackage 170.2.70, `/a:Publish` then `/a:Extract` |

`harbour` also references `master.dacpac`, from the `Microsoft.SqlServer.Dacpacs.Master` package, but it is not here: its
licence lets it be used to develop and test, and distributed only under terms a public repository can't meet. Its absence
is itself tested: harbour is read with one reference missing.

A package keeps where its references were when it was built, as absolute paths on the build machine, so every
reference here records a path that doesn't exist on any other machine. That is the case a reader must handle: the
package is found beside the one that references it, as a build leaves it.

## How they were built

`build.sh` builds each group in the dev container (`fixtures`, `corpus`, `projects`, `versions`, `extract`). It writes an
SDK-style project for a fixture's folder of scripts; the two projects of their own keep their `.sqlproj` in `projects/`.
`extract` deploys to a server of your own (`HARUSPEX_TEST_SQLSERVER`, as for the differential tests), extracts, and leaves
dropping the database to you.

A package's `Origin.xml` records when it was built, so a rebuild changes every file even when nothing else has. Rebuild
only the packages a change needs, and say in the commit which and why: the IR records each package's SHA-256, so a
rebuilt package changes the snapshots that read it.
