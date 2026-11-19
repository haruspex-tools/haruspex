# The unknown reason `hidden-column`

An unknown reason, `IrUnknownReason.HiddenColumn` in the IR's model. A value or a result is unknown because the result has a column the client receives but can't name.

## What it means

A result set carries a column the server marks hidden, which some drivers list among the columns and others leave out, so its keys depend on the client: `FETCH` without `INTO`, whose row has `ROWSTAT`.

## What to do

To change it, declare the results in the config file's `"results"`.

## What it rests on

Observed against SQL Server in `Cursor_statements_and_fetch_into_return_nothing_and_fetch_without_into_returns_one_row` (`DmlObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
