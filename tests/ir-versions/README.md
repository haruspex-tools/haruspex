# IR files, one per version

One IR file from each version of the format, kept so that the reader is tested against what each version really wrote. The
current version's file must be read, and round trip to the same bytes; so must 0.26's, 0.25's, 0.24's and 0.23's, the older versions
the reader still reads, since a document that says less is still labelled with the oldest version that says all it says
([decision 0072](../../docs/decisions/0072-a-document-written-with-a-baseline-is-the-combined-one-and-says-so.md),
[decision 0078](../../docs/decisions/0078-the-ir-says-where-each-field-came-from.md)); every other must be refused, naming its version and the versions the reader
reads (`IrVersionTests`). The reader refuses rather than migrates; the guide's chapter
[The IR as a contract](../../docs/internals/10-the-ir-as-a-contract.md) says why.

Each file is named for its version (`0.15.ir.json`) and holds the IR of the invented procedure `sales.get_order` from the
`orders` fixture, exactly as haruspex wrote it at the commit that set that version, but where the table says otherwise:

| Version | Commit |
|---|---|
| 0.1 | `fe273c8` |
| 0.2 | `97eb734` |
| 0.3 | `aec154a` |
| 0.4 | `f3d3c15` |
| 0.5 | `7bfb0c3` |
| 0.6 | `12ab442` |
| 0.7 | `506ebaa` |
| 0.8 | `871a395` |
| 0.9 | `5de5c7b` |
| 0.10 | `d2c4056` |
| 0.11 | `5501688` |
| 0.12 | `c47163a` |
| 0.13 | `f98e9c4` |
| 0.14 | `3bdcf35` |
| 0.15 | `89756aa` |
| 0.16 | `8a7c00d` |
| 0.17 | `4e7760b` |
| 0.18 | `e5bf149` |
| 0.19 | `06fae1b` |
| 0.20 | `d89c6f1` |
| 0.21 | `d6be159` |
| 0.22 | `f63964c` (set at `6aff87d`; `returnsCode` added at `f63964c`, before 0.22 reached `main`) |
| 0.23 | `5bfdbcbd` |
| 0.24 | `c4093e7d`; a document of one database is still written as 0.23, so this file is `sales.get_order` combined with a baseline in which it didn't return `isPriority` |
| 0.25 | the commit that adds `IrSourceColumn`, on `feature/explain`: each field says the columns it is read from (`from`) |
| 0.26 | the commit that merges `main` into H3 (table types, first set at `645af81a` as 0.25, renumbered when 7b took 0.25); a document that defines none is still written as before, so this file is `till.add_lines` from the `till` fixture, whose `@lines` is a table-valued parameter |
| 0.27 | the commit that adds the kind `bytes`; a document that holds no binary value is still written as before, and that commit infers none yet, so this file is the hand-built document every IR test reads (`IrSamples.Binary`), as `IrJsonWriter` writes it |

**When the version changes**, add the new version's file here, written by the new version, and a row for it. The test fails
until there is one, so that every version the format has ever had is kept. The previous current file then becomes one the
reader refuses, which the same test checks.
