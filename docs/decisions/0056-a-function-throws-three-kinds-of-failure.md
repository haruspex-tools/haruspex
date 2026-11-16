# A function throws three kinds of failure

**Date:** 16 Nov 2026

**Decision.** A generated function fails in one of three ways, and a caller tells them apart:
- **Its parameters were wrong.** It throws a `ParameterError` before the connection is touched (`db.request()` is not called),
  naming the procedure and each failing parameter with its issue.
- **The database raised an error.** mssql's own `RequestError` passes through unchanged, never wrapped: it already carries the
  error's `number`, `state`, `class`, `procName` and `lineNumber`, so a caller's existing handling keeps working.
- **What came back broke the contract.** The schema rejected a result set or the `OUTPUT` parameters: it throws a `ResultError`
  naming the procedure, the part (`the results` or `the OUTPUT parameters`) and each issue's path. The database and the module
  are deployed out of step, which no retry fixes.

Connection failures and timeouts are the driver's, untouched. Each error class is written into every module, and has a stable
`name` (HaruspexParameterError, HaruspexResultError) and a `kind` (`parameters`, `result`), so code that handles errors from
several modules, or doesn't import any, tells them apart without `instanceof`. Every failure is thrown, as idiomatic TypeScript
does; nothing is wrapped in a result type.

**Context.** Observed on 16 Nov 2026 with mssql 12.7.14 on SQL Server 2025 RTM-CU10 (`MssqlFunctionTests`):
- `THROW`, and `RAISERROR` of severity 11 or more, make `execute` reject with a `RequestError`, even after result sets have
  arrived, and even when the procedure goes on to return more (`RAISERROR` doesn't end it). The rejection carries no result
  sets, no `OUTPUT` values and no return code: only the streaming API, which the functions don't use, shows what arrived
  before. So a procedure that fails part-way gives its caller the error alone.
- Severity 10 or less is a message: the call succeeds, with every result set.

**Alternatives.**
- *One error class exported once, by a runtime package every module imports.* `instanceof` would work across modules, but every
  module would need the package, at one version, against "no module imports another" and "fit in; don't impose". A name and a
  kind work across modules and versions with nothing shared.
- *Wrap the `RequestError` in an error of haruspex's*, to name the procedure. The driver's error names it already
  (`procName`), and a wrapped error breaks every `catch` that tests for the driver's.
- *Keep the validator's own error as the `ResultError`'s cause.* The functions check values through Standard Schema, the
  interface Zod, Valibot and ArkType share, so that they stay the same whichever validator wrote the schemas (Roadmap 6);
  Standard Schema hands over issues, not an error. The issues, each with its message and path, are on the error, which is what
  a cause would have carried.
- *Return a result type (`{ ok, error }`).* It would impose a style on every caller, and still have to throw for the driver's
  own failures.

**Room for what comes next.** Row 6r (the errors a procedure raises: the numbers of its literal `THROW` and `RAISERROR`) can
list them in each function's documentation comment, and type a guard that narrows a `RequestError` by number, without
changing any of the above: the driver's error is already the one a caller catches.

**In the code:** `MssqlPrinter`, which writes the error classes and the functions.
