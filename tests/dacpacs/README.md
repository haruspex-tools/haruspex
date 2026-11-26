# Test DACPACs

The DACPACs the tests read: the fixtures and WideWorldImporters built as projects, two small projects of their own, and a
few built or extracted in other ways. They are checked in, so the tests need neither DacFx nor a network. Nothing in the
solution refers to DacFx; it is used only to make these files, by hand, which its licence allows ("to develop and test
your applications"), and they are rebuilt only when a test needs a new one.

## What is here

| Path | What it is | Built with |
|---|---|---|
| `fixtures/<name>.dacpac` | Each fixture that builds as a project, from its `database/` folder: every one but the three `DacpacEquivalenceTests.Unbuilt` lists with the build error that keeps each out (`checks`, `adventure-works`, `wide-world-importers`), which fails if one is missing | `Microsoft.Build.Sql` 2.3.0 (DacFx 170.5.96), SQL Server 2025's provider |
| `corpus/WideWorldImporters.dacpac` | WideWorldImporters, from the items its vendored project builds, with its collation and compatibility level (130) | the same, with SQL Server 2016's provider: the project names Azure SQL Database's, which refuses the storage its scripts declare |
| `atelier/atelier.dacpac` | `projects/atelier`: what the fixtures don't show, such as an alias type, a table type, a check disabled by `NOCHECK`, a unique index created apart, temporal and graph tables, a synonym, `ANSI_NULLS` and `QUOTED_IDENTIFIER` off by item metadata, an encrypted procedure, view, scalar function and inline function with procedures that use them, a computed column `PERSISTED NOT NULL`, a nonclustered primary key and a procedure that finds a row by each kind of key, and a name with `:` and `/` | `Microsoft.Build.Sql` 2.3.0 |
| `gauge/gauge.dacpac` | `projects/gauge`: computed columns, `PERSISTED`, `PERSISTED NOT NULL` and neither, on a table built with `ANSI_NULLS` on and on one built with it off by item metadata, and a procedure that reads them | `Microsoft.Build.Sql` 2.3.0 |
| `lectern/lectern.dacpac` | `projects/lectern`: descriptions in extended properties on a table, its columns, a view, a view's column, a procedure and its parameter, with one under another name and two on what no output has (a schema and a constraint), each value a literal holding a quote, `*/` or a line break | `Microsoft.Build.Sql` 2.3.0 |
| `harbour/*.dacpac` | `projects/harbour` and the packages it references, as its build leaves them in its `bin` folder: `crane` (the same database), `quay` (referenced by `crane`, so harbour reaches it only through it) and `tally` (another database, named by the SQLCMD variable `$(Tally)`, in a collation other than harbour's) | `Microsoft.Build.Sql` 2.3.0 |
| `orchard/*.dacpac` | `projects/orchard` and the packages of its database it references, as its build leaves them: `press` (the same database, built with `QUOTED_IDENTIFIER` off) and `cellar` (referenced by `press` only). `orchard` holds every kind of item a build reads or leaves out: `Build Remove` of a file and of a pattern, `None`, `Content` inside and outside the folder and after a `Build Remove`, pre- and post-deployment scripts and a file a post-deployment script includes with `:r` (the post-deployment script turns a check off and alters a procedure, which a deployment leaves so), a procedure that turns a table's check and unique index off, which a package holds as module text, a `Build` item outside the folder named through a property, per-item `AnsiNulls` and `QuotedIdentifier` by `Update` with backslashes, and a setting from its own `Directory.Build.props` | `Microsoft.Build.Sql` 2.3.0 |
| `mill/*.dacpac` | `projects/mill`, a legacy SSDT project as Visual Studio writes it, and `granary`, the legacy project of the same database it references: `Build` items with backslashes and per-item metadata, a `None`, a post-deployment script, and a `.sql` file it doesn't list | Microsoft.Build.Sql 2.3.0's build targets, on Linux (below) |
| `corpus/WideWorldImporters-ssdt.dacpac` | WideWorldImporters from its own legacy SSDT project file, unchanged, so with its per-item and project settings, which the package above leaves out | the same as `mill`, for SQL Server 2016 (Sql130): its own DSP, Azure SQL Database's, refuses the storage its scripts declare |
| `winery/*.dacpac` | `projects/winery` and every package it refers to, as its build leaves them: `vault` (another database's project, named by the SQLCMD variable `$(VaultDb)`, in a collation of its own), `almanac` (another database's project, named `almanac`), `barrel` (a DACPAC of the same database) and `tasting` (a DACPAC of another database, `$(TastingDb)`), both from `projects/lib`. It refers to `master.dacpac` by the `Microsoft.SqlServer.Dacpacs.Master` package too, which isn't kept | `Microsoft.Build.Sql` 2.3.0 |
| `winery-1.0.0/*.dacpac` | The same, from a copy in which every project names `Microsoft.Build.Sql` 1.0.0, SQL Server 2022's provider and the master package 160.2.8, with `barrel` and `tasting` built the same way (1.0.0 can't load a package 2.3.0 built for SQL Server 2025, SQL72025) | `Microsoft.Build.Sql` 1.0.0 |
| `cooperage/*.dacpac` | `projects/cooperage`, a legacy SSDT project as Visual Studio writes it, and the packages it refers to: `stave` (a legacy project of another database, `$(StaveDb)`), `barrel` (the same database) and `tasting` (another database, named `tasting`). It refers to SSDT's own `master.dacpac` under `$(DacPacRootPath)` too, which the build was given and which isn't kept | as `mill` |
| `projects/lib/*.dacpac` | `projects/barrel` and `projects/tasting`, which winery and cooperage refer to by their paths, as DACPACs a team was given | `Microsoft.Build.Sql` 2.3.0 |
| `master-shape/master.dacpac` | A package in `master.dacpac`'s shape, standing in for it: a `sys` view and an `INFORMATION_SCHEMA` view whose columns are untyped, and a system procedure with a parameter and no text. Its `model.xml` is written by hand in `projects/master-shape` from the shapes of the real package's elements, not copied from it | zipped by `build.sh shapes` |
| `versions/orders-build-sql-<version>.dacpac` | The `orders` fixture under older versions | `Microsoft.Build.Sql` 2.0.0 (DacFx 170.2.70) and 1.0.0 (DacFx 162.5.57), SQL Server 2022's provider |
| `versions/<name>-extract.dacpac` | `atelier` and `orders` deployed to SQL Server 2025 and extracted again, `atelier` after the indexes of `pk_shelf`, `uq_kiln_serial` and `ix_kiln_serial_peak` are disabled, so that it shows what an extract writes of each (decision 0120) | SqlPackage 170.2.70, `/a:Publish` then `/a:Extract` |
| `versions/kiln-clr-extract.dacpac` | A CLR scalar function and procedure (`projects/kiln-clr`, compiled for .NET Framework 4.8), and a procedure that calls the function, deployed by the script `build.sh clr` writes and extracted, since a project's build in the dev container refuses a .NET Framework assembly (SQL70557) | SqlPackage 170.2.70, `/a:Extract` |
| `versions/options-off.dacpac` | `projects/options-off`: every database option off, compatibility level 150 and a collation of its own, which is what a package writes when each differs from DacFx's default | `Microsoft.Build.Sql` 2.3.0, SQL Server 2022's provider |

`harbour`, `winery` and `cooperage` also reference `master.dacpac`, from the `Microsoft.SqlServer.Dacpacs.Master` package or
SSDT's own, but it is not here: its
licence lets it be used to develop and test, and distributed only under terms a public repository can't meet. Its absence
is itself tested: harbour is read with one reference missing. `master-shape/master.dacpac` stands in for it, a quick test on
every run. Microsoft's own package is restored, never committed: the test project references
`Microsoft.SqlServer.Dacpacs.Master` (pinned in `Directory.Packages.props`, its hash in the lock file, so a changed package
fails the restore), and `Microsoft_s_master_dacpac_has_the_shape_the_stand_in_has` reads it where NuGet put it, on every run, CI
included. The maintainer chose this on 14 Nov 2026, over skipping the test where the package happened to be absent.

A package keeps where its references were when it was built, as absolute paths on the build machine, so every
reference here records a path that doesn't exist on any other machine. That is the case a reader must handle: the
package is found beside the one that references it, as a build leaves it.

## How they were built

`build.sh` builds each group in the dev container (`fixtures`, `corpus`, `projects`, `legacy`, `ssdt`, `versions`, `extract`, `clr`, `shapes`, `references`). It writes an
SDK-style project for a fixture's folder of scripts; the two projects of their own keep their `.sqlproj` in `projects/`.
`legacy` and `ssdt` build a legacy SSDT project, which Visual Studio's SSDT builds on Windows, on Linux: with Microsoft.Build.Sql's
own copy of the same build targets, named by `SQLDBExtensionsRefPath` as the project file's own `Import` allows, and
`legacy-on-linux.targets`, which skips the .NET Framework steps a database project doesn't need (its comment says which). The
project file is used as it is, and nothing given changes what is built or its settings.
`extract` deploys to a server of your own (`HARUSPEX_TEST_SQLSERVER`, as for the differential tests), extracts, and leaves
dropping the database to you.

`build.sh` needs the dev container. When it isn't to hand, a package can be built the same way on the host with the .NET 10
SDK and `DOTNET_ROLL_FORWARD=Major` (Microsoft.Build.Sql's tasks target .NET 8): `orchard/*.dacpac` and the fixtures `beads`,
`folds`, `gallery` and `parcels` were built so on macOS on 23 Nov 2026, from the project `build.sh` writes, and `atelier/atelier.dacpac`
and `fixtures/fittings.dacpac` on 24 Nov 2026, and `lectern/lectern.dacpac` in WSL on 26 Nov 2026; `versions/atelier-extract.dacpac` was extracted from atelier's the same day with
SqlPackage 170.2.70 in the .NET SDK's container, as `build.sh extract` does.

A package's `Origin.xml` records when it was built, so a rebuild changes every file even when nothing else has. Rebuild
only the packages a change needs, and say in the commit which and why: the IR records each package's SHA-256, so a
rebuilt package changes the snapshots that read it.
