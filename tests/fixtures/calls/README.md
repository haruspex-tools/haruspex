# Fixture: calls

An invented bakery, ovens and batches, for following `EXEC`: procedures built from other procedures, through
temporary tables, `INSERT … EXEC`, branches and recursion, and one that runs dynamic SQL with its results declared in
`haruspex.json`. No part of it comes from a real system.

| Procedure | Covers |
|---|---|
| `calls.daily_report` | a report composed from helpers: `oven_list`'s rows, then `picked_batches`' rows read from the caller's `#picked`. `add_placeholder`, called on one path, inserts rows that leave `round_no` null, a column that is never null without it |
| `calls.oven_list` | a helper with one result set |
| `calls.picked_batches` | a helper that reads its caller's `#picked`: alone, its columns are `caller-temp-table`; through `daily_report`, they are typed (not run alone) |
| `calls.add_placeholder` | a helper that writes its caller's `#picked` (not run alone) |
| `calls.batch_rows` | two result sets of three columns each |
| `calls.rack_sizes` | two result sets of one column, one nullable `tinyint`, one `int` |
| `calls.batch_feed` | `INSERT … EXEC` staging into a declared `#stage` (which keeps its declaration) and into `#sizes`, made by `SELECT … INTO` from a variable (which takes what `rack_sizes` returns: nullable, of `int`'s range), then `FOR JSON` over each |
| `calls.walk_even`, `calls.walk_odd` | mutual recursion, solved as a fixed point: each returns ovens or batches, a union |
| `calls.ovens_or_batches` | a branch that calls one of two procedures, which return one and two result sets: a union first, then a result that may not run |
| `calls.delivery_stage` | `INSERT … EXEC` without a column list into a table whose identity column `SELECT … INTO` copied: the rows skip it, so `rack_counts`' nullable rack count goes into `n` |
| `calls.rack_counts` | one result set of one nullable column |
| `calls.recount_from`, `calls.recount` | recursion through the caller's `#counts`: each step writes nulls into it before calling itself, so the last step reads a nullable `x` (`recount` alone is not run) |
| `calls.maybe_then_batches`, `calls.maybe_ovens` | a callee whose only result set is on one branch, then a result of the caller's, which arrives first when the callee returns none |
| `calls.search_batches` | dynamic SQL (`sp_executesql`), whose results are declared in `haruspex.json`. `DeclarationAcceptanceTests` shows the generated Zod accepting the real output under the right declaration and rejecting it under two wrong ones |
| `calls.search_with_ovens` | a caller of `search_batches`, whose declared results flow into its own, listed as an override `via` it |
| `calls.oven_cards` | `WITH RESULT SETS` over `oven_list`: its columns renamed and converted, a label longer than the declared `nvarchar(6)` cut to six characters, which haruspex warns of (HAR2104) |
| `calls.search_declared` | dynamic SQL (`sp_executesql`) whose call declares its results with `WITH RESULT SETS`, so they are typed with no declaration in `haruspex.json` |
| `calls.quiet_then_ovens` | `WITH RESULT SETS NONE` over a callee that returns no set on the path taken, then a result of its own |

Its `sql` folder holds one query file, `sql/getBakeSheet.sql`: a thin file that calls `batch_rows` with `WITH RESULT SETS`,
so the file names its function, its `@ovenId` the parameter (an `int`, from `batch_rows`' `@oven`), and the declaration both
sets' fields. The differential tests call it through its generated function, as they call the procedures.
