# Decisions

The decisions haruspex's design rests on, one short record each: when it was made, what was decided, the context, the
alternatives, and why they lost. The code and the guide link here rather than repeat the argument. A record describes the
decision as it stands; when one is reversed, the record says so and links its successor, and is not deleted.

| Record | Date | Decision |
|---|---|---|
| [0001](0001-infer-from-source-never-a-live-database.md) | 7 Nov 2026 | Infer from the source, never from a live database |
| [0002](0002-unknown-with-a-reason-never-a-guess.md) | 7 Nov 2026 | Never claim more than the engine guarantees: unknown, with a reason, never a guess |
| [0003](0003-an-ir-of-its-own.md) | 7 Nov 2026 | An intermediate representation of its own, not JSON Schema |
| [0004](0004-scriptdom-is-the-only-sql-dependency.md) | 9 Nov 2026 | ScriptDom is the only SQL dependency; no DacFx |
| [0005](0005-claim-everything-the-engine-guarantees.md) | 9 Nov 2026 | Claim everything the engine guarantees |
| [0006](0006-layers-with-one-way-dependencies.md) | 9 Nov 2026 | Layers, with dependencies one way and checked by a test |
| [0007](0007-every-engine-rule-is-observed.md) | 9 Nov 2026 | Every rule about the engine is observed on a real server |
| [0008](0008-objects-strip-unknown-keys-by-default.md) | 9 Nov 2026 | Objects strip unknown keys by default; strict is a setting |
| [0009](0009-parameters-are-strict.md) | 9 Nov 2026 | Parameter objects are strict |
| [0010](0010-procedures-are-selected-per-schema.md) | 9 Nov 2026 | Procedures are selected per schema |
| [0011](0011-generated-names-leave-the-schema-out.md) | 9 Nov 2026 | Generated names leave the schema out, and a clash is an error |
| [0012](0012-recursion-is-solved-as-a-fixed-point.md) | 9 Nov 2026 | Recursion is solved as a fixed point |
| [0013](0013-results-that-cannot-be-inferred-are-declared.md) | 9 Nov 2026 | Results that cannot be inferred are declared, and still checked |
| [0014](0014-count-is-nullable.md) | 10 Nov 2026 | `COUNT` is nullable |
| [0015](0015-fetch-without-into-is-unknown.md) | 10 Nov 2026 | `FETCH` without `INTO` is unknown |
| [0016](0016-visible-read-uncommitted-drops-key-claims.md) | 10 Nov 2026 | Visible read uncommitted drops key claims; the caller's isolation is assumed |
| [0017](0017-stored-json-is-typed-only-by-declaration.md) | 11 Nov 2026 | JSON stored in a column is typed only by declaration |
| [0018](0018-snapshots-without-a-snapshot-library.md) | 9 Nov 2026 | Snapshots without a snapshot library |
| [0019](0019-the-guide-is-held-to-the-code-by-tests.md) | 11 Nov 2026 | The guide is held to the code by tests |
| [0020](0020-public-figures-come-from-public-corpora.md) | 11 Nov 2026 | Public figures come only from public corpora, written by a test |
| [0021](0021-fixtures-are-found-never-listed.md) | 11 Nov 2026 | Fixtures are found by listing their folder, never named in a list |
| [0022](0022-values-compare-by-structure.md) | 12 Nov 2026 | Values compare by structure, never by their rendered text |
| [0023](0023-the-ir-schema-is-generated-from-the-model.md) | 12 Nov 2026 | The IR's JSON Schema is generated from the model |
| [0024](0024-an-ir-reader-refuses-other-versions.md) | 12 Nov 2026 | An IR reader refuses a version other than its own |
| [0025](0025-null-reasons-in-neutral-terms.md) | 12 Nov 2026 | Reasons to be null are named for the reason, not the SQL |
| [0026](0026-the-emitter-plans-lowers-and-prints.md) | 12 Nov 2026 | The emitter plans, lowers and prints, and walks over types go through one graph |
| [0027](0027-one-lattice-per-fact.md) | 12 Nov 2026 | One lattice per fact, facts dropped to their top by default, and the type rule named apart from the join |
| [0028](0028-one-fixed-point-and-one-environment-of-facts.md) | 12 Nov 2026 | One fixed-point iteration with one cap, and one immutable environment of what conditions prove |
| [0029](0029-built-in-types-are-one-catalogue-observed-row-by-row.md) | 12 Nov 2026 | Built-in types and conversions are one catalogue of data, observed row by row |
| [0030](0030-built-in-functions-are-entries-with-stable-codes-typed-through-one-table.md) | 13 Nov 2026 | Built-in functions are entries with stable codes, typed through one table |
| [0031](0031-the-model-is-closed-hierarchies-of-records.md) | 13 Nov 2026 | The model is closed hierarchies of records, read from ScriptDom in one place, in source order |
| [0032](0032-a-switch-over-a-closed-hierarchy-is-exhaustive.md) | 13 Nov 2026 | A switch over a closed hierarchy is exhaustive, and the build checks it |
| [0033](0033-binding-is-on-demand-and-kept-for-each-set-of-names.md) | 13 Nov 2026 | Binding is on demand, and kept for each set of names |
| [0034](0034-collections-that-compare-by-value-are-a-layer-of-their-own.md) | 13 Nov 2026 | Collections that compare by value are a layer of their own |
| [0035](0035-the-typer-apart-from-the-walker.md) | 13 Nov 2026 | The typer apart from the walker: expressions as a function of their scope, statements as the flow |
| [0036](0036-diagnostics-have-stable-codes.md) | 13 Nov 2026 | Diagnostics have stable codes, one type and one written form |
| [0037](0037-steps-return-outcomes.md) | 13 Nov 2026 | Steps return outcomes; exceptions are for faults |
| [0038](0038-the-callers-session-is-declared.md) | 14 Nov 2026 | The callers' session options are declared, and not known by default |
| [0039](0039-a-location-in-a-dacpac-names-the-package-and-the-object.md) | 14 Nov 2026 | A location in a DACPAC names the package and the object |
| [0040](0040-a-dacpac-is-read-with-the-packages-of-its-database.md) | 14 Nov 2026 | A DACPAC is read with the packages of its database, found beside it first |
| [0041](0041-a-type-is-spelt-with-its-defaults-written-out.md) | 14 Nov 2026 | A type is spelt with its defaults written out |
| [0042](0042-a-table-of-another-database-is-read-as-its-package-declares-it.md) | 14 Nov 2026 | A table of another database is read as its package declares it |
| [0043](0043-a-value-set-is-canonical-by-a-fold-observed-from-the-server.md) | 14 Nov 2026 | A value set is canonical by a fold observed from the server |
| [0044](0044-a-project-file-is-evaluated-in-part-and-the-rest-reported.md) | 15 Nov 2026 | A project file is evaluated in part, and the rest reported |
| [0045](0045-a-project-s-references-are-read-from-where-its-build-takes-them.md) | 15 Nov 2026 | A project's references are read from where its build takes them |
| [0046](0046-a-sqlcmd-variable-in-a-body-is-unknown.md) | 15 Nov 2026 | A SQLCMD variable in a body is unknown |
| [0047](0047-a-star-is-bound-against-the-tables-the-project-defines.md) | 15 Nov 2026 | A star is bound against the tables the project defines |
| [0048](0048-an-absent-json-result-reads-as-null.md) | 16 Nov 2026 | An absent JSON result reads as null, as the mssql driver hands it over |
| [0050](0050-check-compares-claims-not-files.md) | 16 Nov 2026 | `check` compares claims, not files, and fails when there is nothing to compare |
| [0051](0051-one-json-schema-document-per-module-every-value-as-json.md) | 16 Nov 2026 | One JSON Schema document per module, every value as JSON writes it |
| [0052](0052-a-decimal-s-places-are-a-note-not-multiple-of.md) | 16 Nov 2026 | A decimal's places are a note in JSON Schema, not `multipleOf` |
| [0054](0054-a-parameter-is-checked-against-its-declared-type.md) | 16 Nov 2026 | A parameter is checked against its declared type |
| [0055](0055-dates-and-times-are-sent-as-iso-8601-strings.md) | 16 Nov 2026 | Dates and times are sent as ISO 8601 strings |

## Writing a record

Number it after the last, and name the file after the decision. Keep it short: a date, the decision in a sentence or two,
the context, the alternatives with why each lost, and the reason. Write "we" for the project if it reads better, as
nowhere else in the documentation. A record states only what was decided about haruspex's design and why; history that
explains nothing about the code as it is belongs nowhere.

The code that rests on a decision links its record (`docs/decisions/0014-count-is-nullable.md`) rather than repeating the
argument, and the test in `CodeReferenceTests` checks that every such path resolves.
