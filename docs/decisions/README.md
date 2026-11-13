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

## Writing a record

Number it after the last, and name the file after the decision. Keep it short: a date, the decision in a sentence or two,
the context, the alternatives with why each lost, and the reason. Write "we" for the project if it reads better, as
nowhere else in the documentation. A record states only what was decided about haruspex's design and why; history that
explains nothing about the code as it is belongs nowhere.

The code that rests on a decision links its record (`docs/decisions/0014-count-is-nullable.md`) rather than repeating the
argument, and the test in `CodeReferenceTests` checks that every such path resolves.
