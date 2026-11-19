# The unknown reason `system-procedure`

An unknown reason, `IrUnknownReason.SystemProcedure` in the IR's model. A value or a result is unknown because it calls a system procedure, whose results are not modelled.

## What it means

A system procedure (`sp_…`, `xp_…`), whose results are SQL Server's own. A procedure of the project with such a name may be one too: SQL Server runs its own procedure of that name instead, if it has one.

## What to do

To change it, declare the results in the config file's `"results"`.

## What it rests on

Observed against SQL Server in `A_system_procedures_name_runs_the_system_procedure_whatever_the_project_defines` (`ExecObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
