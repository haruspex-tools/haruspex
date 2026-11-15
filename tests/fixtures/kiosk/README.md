# Fixture: kiosk

An invented chain of market kiosks deployed once per region, for SQLCMD variables in bodies: a literal, a CHECK set, a key and a
JSON document holding `$(Region)` or `$(Label)`, which a deployment replaces with the variable's value before SQL Server sees the
text ([decision 0046](../../../docs/decisions/0046-a-sqlcmd-variable-in-a-body-is-unknown.md)). No part of it comes from a real
system. The differential tests deploy the scripts as written, as a tool that doesn't replace variables would, so the seed
(`tests/fixtures/kiosk/seed.sql`) stores the text `$(Region)`; `SqlcmdVariableObservationTests` deploys the same kind of text
with values of different lengths put in, the empty one too.

| Procedure | Covers |
|---|---|
| `kiosk.whoami` | Literals holding a variable, `varchar` and `nvarchar`, alone and inside other text, and the length of one: unknown; a literal without one beside them, typed |
| `kiosk.list_stalls` | A CHECK set holding a variable (no set) beside one holding none (a set); a CASE of literals, one holding a variable |
| `kiosk.stall_card` | A `json_object` key and a JSON document spelt in a literal, each holding a variable, inside a typed `FOR JSON` object |
| `kiosk.labels` | An alias holding a variable, whose key in the result is the variable's value: the result's keys are unknown (a project's build refuses a variable in a column's name, SQL70604, so a fixture that builds can't have one) |
