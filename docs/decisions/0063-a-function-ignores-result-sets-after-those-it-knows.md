# A function ignores result sets after those it knows

**Date:** 17 Nov 2026

**Decision.** A generated function reads the result sets its procedure lists and ignores any that arrive after them, so a
database that gains a trailing result set before the application redeploys doesn't break it. `"objects": "strict"` makes a
function refuse them, with a `ResultError` (`dbo.get_order, the results: Expected exactly 2 result sets, received 3`), as it
makes a schema refuse a key. A set every call returns that is missing fails under either setting
(`Expected at least 2 result sets, received 1`). What a function returns is unchanged: the same fields, of the same types
([decision 0060](0060-a-call-returns-its-one-result-itself.md) stands). This extends
[decision 0008](0008-objects-strip-unknown-keys-by-default.md) from a result's keys to a procedure's result sets.

**Context.** Agreed with Richard on 17 Nov 2026, after a real application's trial of the npm preview: an application and its
database deploy separately, and in most teams the database goes first. A result gaining a key was already survived, by 0008. A
procedure gaining a result set was survived only by accident of how the functions read their sets, by position, with nothing
said or tested, and nothing refused it under `"strict"`. A missing set, meanwhile, read as `[]` or `null`, which passed a schema
of any number of rows: a set the IR says always arrives could vanish unnoticed.

**Alternatives.**
- *Refuse extra sets by default.* Exact, but every running application would fail on every call of a procedure that gained a
  set, until it was redeployed: the reason 0008 reversed strict objects the day it was decided.
- *Loosen the schemas too*, a tuple with a rest of unknown sets (`z.tuple([...], z.unknown())`, JSON Schema without
  `"items": false`). Every type of several sets would gain `...unknown[]`, which a caller would have to step around, for sets no
  caller reads. The schemas keep describing the sets the procedure lists, and the function hands them only those; an
  application that reads the sets itself takes as many, as `docs/database-module.md` says.
- *Count only under strict.* A set that went missing would still pass as no rows. A database that dropped a set has broken the
  contract whichever way the application deploys, which no setting should hide.

**Reason.** The types stay exact; only the reaction to something unexpected changes, as for keys. A set the procedure always
returns is counted up to the first whose statement may not run (`not-run`): it and those after it may be missing, and the IR's
reader convention pads them. A `FOR JSON` set absent only because no rows matched (`no-rows`) still arrives, as one row of empty
text (observed through mssql), so it counts. Any other reason, such as the config file's declaration, which doesn't say why,
stops the count, so the count checks less rather than ever failing a call the IR allows.

**In the code:** `MssqlOptions.RefusesExtraResultSets`, set by the CLI from `"objects"`; `MssqlFunction.SetsAlwaysReturned`;
the check `MssqlPrinter` writes after each call.
