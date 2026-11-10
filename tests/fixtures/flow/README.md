# Fixture: flow

An invented sales, staff and parts schema for statement flow (slice 6): procedures built from several statements
through temporary tables, table variables, control flow and CTEs. No part of it comes from a real system.

| Procedure | Covers |
|---|---|
| `flow.region_report` | the staged report: `DROP TABLE IF EXISTS`, `SELECT … INTO #t` from a left join, `DELETE` in a branch, `UPDATE … FROM` joining a written value into a column, and `FOR JSON PATH` over the temporary table. The check on `amount` survives the table |
| `flow.region_sales` | accumulation in a table variable inside `WHILE`; the result is the table variable's declaration |
| `flow.sales_by_mode` | a guard that `THROW`s; two branches returning different rows at one position (a union); a `RETURN` that leaves both results out (`not-run`) |
| `flow.part_list` | `CREATE TABLE #t` with a check, `INSERT … SELECT`, `ALTER TABLE … ADD`, `UPDATE … FROM`, and `TRY … CATCH` that rethrows |
| `flow.org_chart` | an employee hierarchy by a recursive CTE |
| `flow.bill_of_materials` | a bill of materials by a recursive CTE with a column list, read by a second CTE; the quantity's check carried through both |
