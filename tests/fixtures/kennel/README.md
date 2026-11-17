# Fixture: kennel

An invented boarding kennel for the facts that reach an expression from the statement around it: a `MERGE`'s actions in its
`OUTPUT`, the rows each action leaves without `inserted`, `deleted` or a source row, a cursor's row as it is when fetched,
what `WHERE` and `HAVING` prove inside a `CASE`, a subquery and an aggregate, `ROLLUP`'s subtotal rows, and `COALESCE` read
under `json_query` or assigned to a variable. Each procedure's result shape depends on one of them, so a fact that stops reaching its expression
changes a snapshot; the seed (`tests/fixtures/kennel/seed.sql`) reaches every case, and the differential tests check each
claim against what SQL Server returns. No part of it comes from a real system.

| Procedure | Covers |
|---|---|
| `kennel.merge_every_action` | `MERGE` with an update, an insert and a delete: `$action` one of three, `inserted` null for a delete, `deleted` for an insert, the source for a row it doesn't match |
| `kennel.merge_two_actions` | an update and an insert: `$action` one of two, `inserted` and the source never null |
| `kennel.merge_joined_source` | a source that is a join: both its sides null for a row the source doesn't match |
| `kennel.update_reads_its_target` | `UPDATE` through a join, `OUTPUT` reading `inserted`, `deleted` and the joined table |
| `kennel.cursor_row` | a keyset cursor's row changed after `OPEN`, which its `WHERE` no longer proves, and a variable its query reads as it was at `DECLARE` |
| `kennel.where_reaches_inside` | `WHERE` proving a column inside a `CASE` and inside a subquery of the outer row |
| `kennel.rollup_and_having` | a grouped column null in `ROLLUP`'s subtotal rows outside the aggregates, not inside them; `HAVING` proving an aggregate |
| `kennel.coalesce_under_json_query` | `COALESCE` of JSON built here, and a `CASE` over a `FOR JSON` subquery, which is JSON only under `json_query` (without it, whether it nests depends on whether the server folds the `CASE`), under `json_query` |
| `kennel.coalesce_into_variable` | `COALESCE` of an aggregate's JSON and `json_array()` assigned to a variable, which holds the same text whether or not the server folds it |

An `OUTPUT` reading the write's target through its alias in `FROM` is not here: SQL Server creates the procedure and refuses
the statement when it runs (4104), so no run could check it.
