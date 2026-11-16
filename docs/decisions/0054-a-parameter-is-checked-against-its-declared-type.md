# A parameter is checked against its declared type

**Date:** 16 Nov 2026

**Decision.** A procedure's parameter keeps its declared type's facts in the IR (a length, digits, a range), in the
database's default collation, as a column of that type would. They are true of the value inside the body, since SQL Server
converts what a caller sends to the declared type. And they are the rule a client checks a value against before sending
it, because SQL Server doesn't refuse a value that breaks them: it cuts it short or rounds it. The generated client module
states that rule as a strict schema for each procedure's parameters.

**Context.** Observed on 16 Nov 2026 with mssql 12.7.14 (tedious 20.3.9) on SQL Server 2025 RTM-CU10
(`ParameterObservationTests`):
- a string longer than `nvarchar(n)`, `varchar(n)` or `nchar(n)` is cut to `n`, without an error, when the driver sends it as
  `max` or chooses its type itself. Sent with the declared length, it is refused with error 8016, "the incoming tabular data
  stream is incorrect", which names neither the procedure nor the parameter;
- a decimal of more places than its scale is rounded, without an error, whether the driver or the server converts it; past
  its precision it is refused. `money` loses a fifth place, cut by the driver or rounded by the server;
- an `int` parameter drops a fraction, `bit` is 1 for any number but 0, and NaN is sent as NULL, each without an error;
- a value out of an integer type's range is refused by the driver (`EPARAM`), and one out of `real`'s by the server.

So for strings and decimals the client's check isn't a second copy of a check the database makes: it is the only one. The
facts describe the value after conversion; for a client they are the same rule read the other way: a value inside them
arrives as it was sent, and a value outside them doesn't.

**Alternatives.**
- *Leave the facets out, and let the client emitter work them out from the type's name (`IrScalar.Sql`).* The emitter would
  then hold a second copy of what each type holds, which Semantics already knows (`SchemaRules.TypeFacts`), and the IR would
  claim less than it knows.
- *Narrow a parameter by what the body proves* (a guard that throws when it is NULL, or out of a range). A guard says what
  the procedure does with a value, not what a caller may send. Nullability is the exception the design names: a guard that
  ends the call when a parameter is NULL means NULL is never useful, and the schema may say so. That is the guards' work,
  not this record's.

**Reason.** The facts are already proven, cost nothing to carry, and give every emitter the same rule. The client can then
refuse a value with an error that names the procedure and the parameter, before any connection is touched, instead of
sending one SQL Server would silently change.

**In the code:** `SqlTypes.ToParameterShape`; the IR's README, "Facets".
