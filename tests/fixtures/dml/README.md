# Fixture: dml

An invented parts depot for writes: DML on permanent tables, `OUTPUT`, triggers, `WITH` before a write, and
cursors. No part of it comes from a real system. Every column that may be null is null in some seeded row a procedure
returns (`tests/fixtures/dml/seed.sql`), and each run that writes runs in a write session that is rolled back.

| Object | Covers |
|---|---|
| `depot.log_error` | the shared logger every `CATCH` calls: an `INSERT`, no result set |
| `depot.list_parts` | the case that motivated reading writes: a read whose `CATCH` calls the logger. Fully typed |
| `depot.get_order` | the same with a `FOR JSON` result |
| `depot.place_order` | `INSERT … OUTPUT` without `INTO` (its rows, with a NOT NULL identity), `OUTPUT … INTO` table variables, an `UPDATE` of a table whose trigger returns nothing, a transaction, and the logger |
| `depot.cancel_order` | `UPDATE … OUTPUT` of `deleted` and `inserted`, and `DELETE … FROM` with a join |
| `depot.sync_prices` | `WITH` before `MERGE`, and its `OUTPUT`: `$action` as the set of its actions, and the columns null for the actions that give them none (`merge-action`), a CTE's proof that a column is not null included |
| `depot.restock` | a cursor: `DECLARE`, `OPEN`, `FETCH … INTO` in a `@@FETCH_STATUS` loop, `CLOSE`, `DEALLOCATE`; each `UPDATE` fires the audit trigger, which returns nothing |
| `depot.purge` | `TRUNCATE TABLE`, `DELETE` through a CTE (cascading to `depot.stock`, whose trigger is not for deletes), `INSERT … EXEC` into a permanent table |
| `depot.recent_note` | the procedure `depot.purge` inserts the results of |
| `depot.add_note` | a write to a table whose trigger returns a result set: results unknown (`trigger-results`) |
| `depot.peek_parts` | `FETCH` without `INTO`: results unknown (`hidden-column`) |
| `depot.prune_prices` | `MERGE` with a joined `USING`: every source of the join is null for the rows not matched by source |
| `depot.low_floor` | a variable read in a cursor's query: its value at `DECLARE`, not at `FETCH` |
| `depot.add_note_by_synonym` | a write through a synonym (`depot.legacy_note_alias`) fires its table's trigger: `trigger-results` |
| `depot.pick_order_lines` | `UPDATE` of an alias in `FROM`, joined: `inserted` and `deleted` typed from the table |
| `depot.stock_audit` | a trigger that writes another table and returns nothing |
| `depot.legacy_note_echo` | a trigger that returns a result set, which SQL Server allows by default |
