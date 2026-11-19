# The unknown reason `trigger-results`

An unknown reason, `IrUnknownReason.TriggerResults` in the IR's model. A value or a result is unknown because a trigger it fires may return results of its own.

## What it means

A statement fires a trigger of the project that may return result sets of its own, which arrive among the procedure's. SQL Server allows it unless the server's `disallow results from triggers` option is on, which it is not by default.

## What to do

To change it, declare the results in the config file's `"results"`.

## What it rests on

Observed against SQL Server in `Results_from_triggers_are_allowed_by_default` (`DmlObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
