# Fixture: assays

An invented laboratory's samples and their measurements, for the loss of precision: a decimal rounded to fewer places, which
a caller with `NUMERIC_ROUNDABORT` on gets as NULL when `ANSI_WARNINGS` and `ARITHABORT` are off, and under `TRY_CAST`
whatever they are ([decision 0038](../../../docs/decisions/0038-the-callers-session-is-declared.md)). It declares no session,
so every rounding is nullable (`precision-loss`), and it guards the other side as well: what keeps every place, or rounds
where the option doesn't look, must not be. No part of it comes from a real system. The seed
(`tests/fixtures/assays/seed.sql`) has a sample with places past every target and one with only zeros there, which rounds
all the same; the runs call each procedure under the driver's own session, under `NUMERIC_ROUNDABORT` on with both others
off, where each rounded value is NULL, and under `NUMERIC_ROUNDABORT` on alone, where a rounding would fail the call.

| Procedure | Covers |
|---|---|
| `assay.rounded_readings` | `CAST` and `CONVERT` to fewer places, of a decimal, money and a string; `COALESCE`, `CASE`, `IIF` and `UNION ALL` to a common type of fewer places; a product, a quotient and a sum whose scale is cut past 38 digits; `AVG` grouped and over a window; `LAG`'s default; an inline function's argument; a variable's declared value |
| `assay.exact_readings` | the guards: a widening `CAST`, `decimal(10,2) + decimal(10,2)`, a decimal and a string literal that fit, a decimal to `int` and to `money`, a float to a decimal, money to four places, `ROUND`, a product and a quotient within 38 digits, money's arithmetic, `%`, `SUM`, `AVG` of money |
| `assay.attempted_readings` | `TRY_CAST` to fewer places and to more |
| `assay.silent_rounding` | a body's `SET NUMERIC_ROUNDABORT OFF`, passed over |
