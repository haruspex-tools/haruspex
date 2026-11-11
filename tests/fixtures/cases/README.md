# Fixture: cases

An invented small bicycle repair desk for `CASE`, `IIF`, `CHOOSE`, `NULLIF`, and `COALESCE` and `ISNULL` over JSON. No
part of it comes from a real system. The seed (`tests/fixtures/cases/seed.sql`) takes every branch of every expression: each job
status, a priority that is NULL, 1, 2, 3 and 4 (beyond `CHOOSE`'s three values), a job finished late and ones not, a phone,
an empty phone and none, a note, a note row whose note is NULL and jobs without one, a customer without jobs, a job without
part lines, a line without a price and one priced 0.

| Procedure | Covers |
|---|---|
| `desk.job_board` | a simple `CASE` of literals (an exact set, never null); a searched one testing `IS NULL` then a comparison; `CASE WHEN x IS NULL THEN 0 ELSE x END` (never null) beside `CASE WHEN x = 1 THEN 0 ELSE x END` (null: UNKNOWN is not FALSE); `IIF`; `CASE` without `ELSE` (`no-else`) and with a `NULL` result (`null-literal`); `CHOOSE` over a `CHECK`ed index in range (never null), the same index with fewer values (`out-of-range` alone, seen null for a tier-3 customer) and a nullable one; `NULLIF` of a column and of an integer literal (`tinyint`); a left join's row proved by a branch |
| `desk.job_card` | one job as JSON, its lines four ways: `COALESCE(… FOR JSON PATH, '[]')` (escaped text, `read-twice`), `ISNULL(…, '[]')` (JSON, never null, no `minItems`), `JSON_QUERY(COALESCE(…))` (JSON again), `ISNULL(…, '{}')` (a union with the empty object), and the first line `WITHOUT_ARRAY_WRAPPER` with `'{}'` as text and through `JSON_QUERY`; inside the lines, `CASE` over arithmetic and `IIF` with `OR` |
| `desk.customer_jobs` | the same idiom as a rowset column: `ISNULL` JSON text never null, `COALESCE` `read-twice` |
| `desk.status_totals` | `ROLLUP`: `CASE WHEN status IS NULL THEN 'all' ELSE status END` never null (the super-aggregate row tested), `CASE` over `SUM` and `MAX` (each computed once), and over a parameter |
