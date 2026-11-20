# A body's own table has the keys SQL Server enforces on it at each statement

**Date:** 20 Nov 2026

**Decision.** A temporary table's keys are part of the walk's state, statement by statement, as its columns are: those its
`CREATE TABLE` declares, none for one `SELECT … INTO` or `CREATE TABLE … AS SELECT` made, one more for each `ALTER TABLE … ADD`
of a key and each `CREATE UNIQUE INDEX` without a filter, one fewer for each drop, and one enforcing nothing between `ALTER
INDEX … DISABLE` and `REBUILD`. A key added holds from its statement on. Where paths meet, a key holds only if every path has
it. A drop or a disable of a name the body doesn't know removes or disables every key SQL Server named. A trigger a write may
fire runs in the procedure's session, so a temporary table it may change is unknown after the write. A table variable has the
keys it declares. A global temporary table (`##t`) claims nothing, its keys included. A string key is used whatever collation
`tempdb` has.

**Context.** A procedure that stages rows in `#lines`, keys them and reads one back was claimed "any number of rows" wherever it
read the key: a scalar subquery, an `OUTER APPLY`, a `FOR JSON … WITHOUT_ARRAY_WRAPPER` or a one-row result was claimed less
than the server guarantees. What the server does was observed (`RefinementObservationTests.TemporaryKeys.cs`): every way of
declaring or adding a key is enforced as a table's is; `SELECT … INTO` makes none, even of an identity; a drop, a disable and an
index that replaces it by `DROP_EXISTING` end it, and `REBUILD` enables it again; a write that would break one fails its whole
statement, `INSERT … EXEC` and `MERGE` included; a key a callee adds to its caller's table stays.

**Alternatives.**
- *Only the keys `CREATE TABLE` declares.* Simple, but the common idiom, `SELECT … INTO` and then a key, would claim nothing.
- *A key added is joined with the state before it, since the statement may fail.* Rows that already share the key, or NULL in a
  primary key's column, refuse it (1505, 8111). But the error fails the call (it ended the procedure and the batch, observed), or
  reaches `CATCH` from the state before the statement, so no path that goes on past it lacks the key. Joining would throw the key
  away for nothing.
- *Keep a key's name where paths name it differently, or tell a constraint from an index.* A later drop by one name would then
  keep a key the other path dropped. A key of no known name, removed by any drop of an unknown name, claims a little less, and
  only after paths that disagree.
- *Claim a global temporary table's keys.* Its key holds whoever writes, but another session may drop it and create another of
  that name between two statements (observed), so nothing about it, its columns included, holds from one statement to the next.
- *Use a string key only under a known collation.* A temporary table's string column takes `tempdb`'s collation, the server's,
  which the project doesn't give (a table variable's takes the database's; observed). But a comparison with a variable or a
  literal is made under the column's own collation, which is the key's equality, and one with a column of another collation is
  refused (468), so the rule that types the value (`Cardinality.InKeysType`) holds without knowing it. Not so a string column of
  a CTE or a derived table: a `COLLATE` inside it stays explicit outside it, and the comparison is made under it (observed), so
  such a column fixes no string key.

**Reason.** Each claim is what the server enforces at that statement on every path that reaches it, and each statement that may
end a key is read as ending it. Nothing about the IR changes: a key is the walk's, read by the cardinality rules a table's keys
already feed.

**In the code:** `KnownTable.Keys`, `KnownTable.JoinKeys`, `TempColumn.RefusesNull`, `Walker.Added`, `Walker.IndexCreated`,
`Walker.IndexAltered`, `Walker.Triggered`, `Typer.TemporaryKeys`, `Typer.TableVariableValues`; the Inference README, "A
temporary table's keys"; `RefinementTests.TemporaryKeys.cs`, `RefinementObservationTests.TemporaryKeys.cs`.