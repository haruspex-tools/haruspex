# A baseline fits when the running application accepts every value the database can give it

**Date:** 18 Nov 2026

**Decision.** Whether one IR fits within another (`IrFit`) is asked of an application built against one document meeting a
database described by another, and judged on the IR's claims. Values flow two ways, and each is judged in its own
direction:

- **Results flow to the application**, so they fit when every value the database can return is one the application's types
  accept. A value that may now be null, a key or a result set that went, a key that may now be left out, a result that may
  now be absent, a type of another kind, and a length, range, number of digits, number of rows or value set that widened are
  each refused. The reverse of each (a value proved never null, a shorter string) fits. A key the application has as left out
  when null is written as optional and never null, so one that may now arrive as `null` (`INCLUDE_NULL_VALUES` added) is
  refused too. A result is judged absent as the application's function counts it (decision 0063): a `FOR JSON` set absent only
  for want of rows still arrives, so the function counts it on every call, and one that may now not be sent at all is refused. Integers of another size are compared
  by the range each allows, since every schema checks them as numbers; a `bigint` is refused either way, since a row delivers
  it as a string.
- **Parameters flow to the database**, so they fit when everything the application may send is something the procedure takes.
  A new parameter fits only with a default, and is then ignored. One removed is refused, with or without a default, since the
  application may send it and SQL Server refuses the call (error 8144). A default lost, a parameter no longer `OUTPUT` (error
  8162) and a type narrowed (`int` to `tinyint` fails with error 8114 on a value past 255; a string cut short is cut silently)
  are refused. A type widened (`int` to `bigint`) fits, since the server converts what is sent. A date or time is compared by
  the range and fraction of its SQL type, which the IR keeps and has no other fact for: `datetime2(7)` to `smalldatetime`
  fails with error 8114 on a date past 2079, and to `datetime2(0)` rounds the fraction without an error, so both are refused;
  a type outside the list decision 0041 spells can't be compared, and a change of it is refused. In a result the IR claims no
  range or fraction for a date, and the schemas check none, so there it is compared by kind and format alone.
- **What the application doesn't know is ignored**, as its generated code ignores it: a key its type doesn't list (decision
  0008), a result set after those it knows (decision 0063), a parameter it doesn't send that has a default, a procedure it
  doesn't call. Under `"objects": "strict"` a key and a result set are refused, since the code then refuses them.
- **What can't be shown to fit doesn't.** A value that became unknown, a length counted in another unit, a value set matched
  under another fold, results that can no longer be listed: each is refused. A result the application had as unknown accepts
  anything, as its schema does. A parameter the procedure has as unknown is refused, since nothing is known of what it takes
  (`int` to `hierarchyid` fails with error 206), unless the application had it as unknown too and both documents say the
  unknown alike, with the same reason and detail, which for a type not modelled names it: an unchanged `xml` parameter fits,
  and `hierarchyid` to `geography` doesn't. Refusing every unknown on both sides would fail every module with such a parameter
  on every check, for a change nobody made.
- **Result sets are matched by place**, as the server sends them. A set's name (decision 0070) is the key the application's
  own function returns it under, which the running application keeps, so a name given, changed or taken away is no finding
  here: it changes the application's next build, which `check` warns of as a return shape (decision 0069).

Each finding is said as the change `IrComparison` words (`results[0][].note: became nullable`), the application's claim
before and the database's after, with whether the application refuses or ignores it and why.

**Context.** An application and its database deploy separately, so for a while the application already running meets the
database being deployed, or the reverse. A real application's trial found this the blocker: a procedure that gained a second
result set broke the running application's calls. The operation is asked both ways round: `haruspex check --baseline` with the
baseline as the application, and, later, `generate` making types that hold for both. It generalises the claim comparison of
[decision 0050](0050-check-compares-claims-not-files.md), which reports every change, to a direction, which reports only the
changes that matter to one side.

**Alternatives.**
- *Judge each emitter's output, not the IR.* The Zod module leaves some claims as comments (a value set matched ignoring case,
  a decimal's places), and the JSON Schema checks others. But the claim is what the application may rely on, in code a schema
  doesn't check (a `switch` over a value set, a non-null assertion), and an emitter added later may check it. So a grown value
  set is refused even where the Zod module would let the new value through.
- *Refuse only a removed parameter the application required.* A parameter with a default may be sent all the same, and a call
  that sends it fails, so its removal can't be shown to fit.
- *Compare integer formats exactly.* A `smallint` that becomes `tinyint` would be refused, though every value fits.
- *Treat a key that went as fitting when the application's type lists it as optional.* It is: a key the application's schema
  allows to be left out may go, since it then never arrives.

**Reason.** Fit is a soundness claim, like every claim haruspex makes: a change it passes must not break the application
already running. `BaselineFitTests` holds it to the server: each pair of procedures is deployed as the baseline and as the
candidate, and the baseline's generated functions call the candidate. A call returns exactly when nothing is refused, and each
refused pair fails on a real value, as its finding says.

**In the code:** `IrFit`, `IrFitFinding`, `IrFitVerdict`, `IrFitOptions`; `IrFitTests` and `BaselineFitTests`.
