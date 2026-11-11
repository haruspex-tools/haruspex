# FETCH without INTO is unknown

**Date:** 10 Nov 2026

**Decision.** A `FETCH` without `INTO` makes the procedure's results unknown, with the reason `hidden-column`.

**Context.** `FETCH` without `INTO` returns the cursor's row as a result set. That row also carries `ROWSTAT`, a column the
server marks hidden: SqlClient lists it among the columns, and a driver that honours the flag leaves it out (observed:
`Cursor_statements_and_fetch_into_return_nothing_and_fetch_without_into_returns_one_row`).

**Alternatives.**
- *Type the row with `ROWSTAT`.* It would claim a column that most callers don't expect, and that some drivers never deliver.
- *Type it without.* It would leave out a column that SqlClient reports.

**Reason.** Which keys the row has depends on the client, so no shape holds for every caller. Real procedures fetch `INTO`
variables, which haruspex types, so this costs little.

**In the code:** `IrUnknownReasons.HiddenColumn`, applied in `ProcedureAnalyzer.Dml.cs`; the Inference README, "Cursors".
