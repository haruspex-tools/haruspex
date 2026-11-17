# Fixture: flow

An invented sales, staff and parts schema for statement flow: procedures built from several statements
through temporary tables, table variables, control flow and CTEs. No part of it comes from a real system.

| Procedure | Covers |
|---|---|
| `flow.region_report` | the staged report: `DROP TABLE IF EXISTS`, `SELECT … INTO #t` from a left join, `DELETE` in a branch, `UPDATE … FROM` joining a written value into a column, and `FOR JSON PATH` over the temporary table. The check on `amount` survives the table |
| `flow.region_sales` | accumulation in a table variable inside `WHILE`; the result is the table variable's declaration |
| `flow.sales_by_mode` | a guard that `THROW`s; two branches returning different rows at one position (a union); a `RETURN` that leaves both results out (`not-run`) |
| `flow.part_list` | `CREATE TABLE #t` with a check, `INSERT … SELECT`, `ALTER TABLE … ADD`, `UPDATE … FROM`, and `TRY … CATCH` that rethrows |
| `flow.part_counts` | `CREATE TABLE #t WITH (DISTRIBUTION = ROUND_ROBIN) AS SELECT` from a left join: the query's columns, as `SELECT … INTO` makes them, nullable on the optional side, with the checks on `unit` and `quantity` |
| `flow.org_chart` | an employee hierarchy by a recursive CTE; `lead` is never null in the anchor and null only through the recursive member |
| `flow.staff_moves` | a `SELECT … INTO` column from a variable (never null here, nullable to SQL Server) made nullable by an `UPDATE` on one path only, read after the paths join |
| `flow.chain_up` | a `WHILE` loop that inserts into a temporary table, writing nulls into a column that started never null |
| `flow.notes_or_names` | two branches returning JSON objects of different keys, one left out when null: the Zod union keeps each object's keys whichever branch produced it |
| `flow.caught_report` | `TRY … CATCH` whose `CATCH` returns a result after an error at any of three points: unions at each position, and later results that may not run |
| `flow.bill_of_materials` | a bill of materials by a recursive CTE with a column list, read by a second CTE; the quantity's check carried through both |
| `flow.sale_card` | guards: a `THROW` when the parameter is NULL, and `@@ROWCOUNT = 0` straight after a `SELECT` that may assign nothing, which proves every variable it assigns; a `COUNT_BIG` assigned from an aggregate without `GROUP BY`, even over no rows |
| `flow.sale_receipt` | an existence guard read with `UPDLOCK, HOLDLOCK` in a transaction the procedure began: the reads before its `COMMIT` find the sale, and one after it may not (`read-twice` at the guard) |
