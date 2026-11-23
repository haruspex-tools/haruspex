# A call returns its one result itself

**Date:** 16 Nov 2026

**Decision.** A generated function whose procedure or query file has one result set, no `OUTPUT` parameters and no return code
worth handing back returns that result itself: `const order = await getOrder(pool, { OrderID: 42 })`. Any other returns an
object of named fields, as before: `result`, or `result1`, `result2` and so on for several result sets; `output` for the `OUTPUT`
parameters; and `returnValue` only when a call may end with a code other than 0. One with nothing to hand back returns nothing
(`Promise<void>`). A call may end with a code other than 0 when its body has a `RETURN` whose value isn't the constant 0 or NULL,
at any depth, or can't be read; the IR records it as `returnsCode` (0.22). The maintainer, 16 Nov 2026, asked for the first form.

**Context.** Most procedures return one result set and nothing else, and every call of their functions destructured a
`result` out of an object whose `returnValue` was always 0. Observed through the mssql driver: no `RETURN`, a bare `RETURN`,
`RETURN 0` and `RETURN NULL` all give `returnValue` 0, and `RETURN 7` gives 7; a query file's batch, run through `sp_executesql`,
may not `RETURN` a value at all (error 178).

**Alternatives.**
- *One shape for every function*, as before. Uniform, but every call pays for the rare procedure with output parameters or a
  meaningful code, and a code that is always 0 suggests it means something.
- *Always the result, with the rest on a second channel* (a property on the array, or a callback). Nothing in TypeScript does
  that cleanly for a `FOR JSON` result, which may be `null`.
- *Decide by the declared types alone*, not the body. A procedure's code is not declared anywhere, so the body is the only
  evidence; when it can't be read, the code is kept.

**Reason.** The common call reads as what it is, and the shape still says everything the procedure can hand back. Whether a
code is kept errs towards keeping it: a `RETURN` with a value on a path never taken only adds a field. A function's shape changes
when its procedure starts or stops returning a code, or gains an `OUTPUT` parameter or a result set, so `haruspex check` reports
each (`returnsCode` among them) and the compiler finds every caller to change.

**Amended** (18 Nov 2026, [decision 0070](0070-a-procedure-s-result-sets-are-named-all-or-none.md)). A procedure or query file
whose sets the config file names (`sets`, or `-- @sets`) returns an object of them by name, `{ order, lines }`, even when it has
one set and nothing else: naming the sets is how a team says the procedure will grow, so a set added later is one more key
rather than this decision's change from the set itself to an object.

**In the code:** `ReturnCodes` (Inference), `IrObject.ReturnsCode`, and `MssqlFunction.ReturnsResultAlone` and
`MssqlFunction.ReturnsNothing` in the mssql emitter.
