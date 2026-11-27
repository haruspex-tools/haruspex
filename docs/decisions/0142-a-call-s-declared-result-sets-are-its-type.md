# A call's declared result sets are its type

**Date:** 27 Nov 2026

**Decision.** `EXEC … WITH RESULT SETS` types the call by its declaration, whatever the call runs: a procedure of the project,
one haruspex can't read, a system procedure or dynamic SQL. The server holds the call to it, so a call that succeeds returned
exactly the declared sets, each present, each column of its declared type, and never null only where `NOT NULL` is written. Each
column claims its type's facets, as a value converted to it does, and nothing of the callee's value. A column declared without
`NULL` or `NOT NULL` is nullable, since it follows the caller's `ANSI_NULL_DFLT`, and so is one of an alias type declared
`NOT NULL`, which adds nothing there. A nullable column's reason is `nullable-column`, at the column of the declaration
(`racks of result set 1 declared at procedures/oven_cards.sql:5:92`). `NONE` is no result set. `AS OBJECT` a table and `AS TYPE`
a table type, named with its schema, give its columns and their `NOT NULL`; a view's or a function's, a computed or hidden column,
a name without its schema and `AS FOR XML` give that set an unknown (`unsupported-query`), and leave the others typed.

Where the callee is a procedure of the project, its results are checked against the declaration. One that never matches (no
path returns as many sets, or every set at a position has another width) fails every call, so no path continues past it and it
is reported (HAR2105). A declared column narrower than the callee's is a lint, reported and still typed by the declaration
(HAR2104). Nothing about where a shape came from is recorded in the IR, so it needs no new version.

The rules are observed on SQL Server 2025 in `ExecObservationTests.ResultSets.cs`: the count (11535, 11536), the width and a
type with no conversion (11537, 11538, before any row), conversion as `CAST` converts (a string cut short, a `tinyint`, `smallint` or `int` too long for `char` or `varchar` written `*`, an
error otherwise even under `ANSI_WARNINGS` off), `NOT NULL` (11553, after the rows before it), the default under
`ANSI_NULL_DFLT_OFF`, the collation, dynamic SQL, errors caught by a caller's `CATCH`, the forms, and `INSERT … EXEC … WITH
RESULT SETS`, a syntax error.

**Context.** A call's results were unknown under any `WITH RESULT SETS` but `UNDEFINED`, which the walk read as a statement not
modelled. The clause is the one place T-SQL lets a caller state the shape of what it calls, and the server enforces it, so it
types calls nothing else can: dynamic SQL among them, which otherwise needs a declaration in the config file. It is also how a
thin query file names a procedure's fields in the application's words, without a naming rule
([the database module](../database-module.md#naming-a-calls-results-with-with-result-sets)).

**Alternatives.**
- *Keep the callee's facts where the conversion preserves them* (the same type, a wider string), so a declared `int` over a
  callee's column of a closed set keeps the set. It claims more, but each case needs its conversion observed, and a declaration
  is usually written to rename, not to narrow. Left for later, with a test per conversion.
- *Take a declared column's nullability from the callee* where the callee's is never null. A conversion sits between them: under
  `ANSI_WARNINGS` off a `CAST`'s overflow is NULL, and only the observed cases are known not to be. A declaration that means
  non-null says so with `NOT NULL`, which the server enforces.
- *A null reason of its own*, such as `declared-result-column`. `nullable-column` already says "declared nullable, or not declared
  `NOT NULL`", which is this case, and a new reason is an IR change every reader must learn.
- *End every path whose callee may not match*, rather than only those that never can. A path that may fail is a path the IR
  already allows for, since every claim is about calls that succeed.
- *Read a view's columns from inference.* What inference proves of a view's column can be narrower than the server's metadata,
  which is what the server enforces; claiming the narrower would be unsound.

**Reason.** The server's enforcement is the proof: every claim made rests on an error the server raises otherwise, each observed,
and anything the declaration doesn't settle is either nullable or unknown with a reason.

**What it costs.** A declared column claims less than the callee's column may prove (no value set, no bound beyond its type's),
and a call's temporary tables are still forgotten where the callee can't be read: the declaration says nothing of them.

**In the code:** `Walker.ResultSets.cs` (the declared sets, the checks against a known callee), `Typer.ResultSets.cs` (each set's
columns), `Walker.Call`; `ExecTests.WithResultSets.cs`, `QueryFileTests`, the `calls` fixture's `oven_cards`, `search_declared`,
`quiet_then_ovens` and `sql/getBakeSheet.sql`; the rules in
[the inference reference](../../src/Haruspex.SqlServer.Inference/README.md#result-sets-a-call-declares).
