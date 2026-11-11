# COUNT is nullable

**Date:** 10 Nov 2026

**Decision.** `COUNT` is typed nullable, with the reason `count-overflow`, unless a `HAVING` clause compares it. `COUNT_BIG`
is never null.

**Context.** `COUNT` returns `int`. Past 2,147,483,647 rows it raises an error by default, but returns NULL when the caller's
session has `ANSI_WARNINGS` and `ARITHABORT` off (observed:
`Count_over_more_rows_than_int_holds_is_null_under_the_callers_ansi_warnings_and_arithabort_off`). A procedure can't stop
its caller doing that.

**Alternatives.**
- *Non-null*, as intuition says and as first planned. It would claim more than the engine guarantees.
- *A special case in haruspex* to assume the default options. The options belong to the caller, whom haruspex never sees.

**Reason.** The principle settles it: a non-null `COUNT` could be null. The way out is in the SQL, not in haruspex: use
`COUNT_BIG`, or override the field in the config file with a reason. Users will notice the `.nullable()`, so the
documentation says why wherever a count is shown.

**In the code:** `IrNullRules.CountOverflow`, applied in `Refinement.cs`; the Inference README, "Aggregates and scalar
subqueries".
