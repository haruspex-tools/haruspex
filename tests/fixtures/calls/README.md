# Fixture: calls

An invented bakery, ovens and batches, for following `EXEC` (slice 6b): procedures built from other procedures, through
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
| `calls.search_batches` | dynamic SQL (`sp_executesql`), whose results are declared in `haruspex.json`. `DeclarationAcceptanceTests` shows the generated Zod accepting the real output under the right declaration and rejecting it under two wrong ones |
| `calls.search_with_ovens` | a caller of `search_batches`, whose declared results flow into its own, listed as an override `via` it |
