# The unknown reason `caller-temp-table`

An unknown reason, `IrUnknownReason.CallerTempTable` in the IR's model. A value or a result is unknown because it reads a temporary table its caller may create, whose shape depends on who calls.

## What it means

A temporary table the procedure does not create on every path before it reads it: a table of that name created by the caller, if there is one, whose shape depends on who calls.

## What to do

To change it, declare the results in the config file's `"results"`.

## What it rests on

Observed against SQL Server in `A_callee_writes_its_callers_temporary_table_and_the_rows_stay` (`ExecObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
