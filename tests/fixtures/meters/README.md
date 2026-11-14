# Fixture: meters

An invented set of electricity meters and their daily readings, for a declared session: its `haruspex.json` says the callers
connect with `ANSI_WARNINGS` on and `ARITHABORT` off, as SqlClient does at login, and the differential tests call it so. So
every overflow, division by zero and `COUNT` past `int` is an error that fails the call, and none makes a value nullable
([decision 0038](../../../docs/decisions/0038-the-callers-session-is-declared.md)). No part of it comes from a real system.
The seed (`tests/fixtures/meters/seed.sql`) has a meter without a site and one without readings, and no value that overflows.

| Procedure | Covers |
|---|---|
| `meter.usage_summary` | `COUNT`, `SUM`, `AVG`, `MAX - MIN`, `SUM / COUNT` and a `CAST` to fewer digits, each never null; string `+` with `ISNULL` |
| `meter.running_totals` | `YEAR`, `* 1000`, a `CAST` to `smallint`, a running `SUM` and a `COUNT` over a window, each never null |
| `meter.meter_card` | `FOR JSON`: a `COUNT` subquery and a product inside a nested array, each a key that is always there |
