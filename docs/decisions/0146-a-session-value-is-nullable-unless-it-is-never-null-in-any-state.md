# A session value is nullable unless it is never NULL in any state

**Date:** 19 Nov 2026

**Decision.** Each value a call reads from its session or its database (`@@ROWCOUNT`, `@@TRANCOUNT`, `@@ERROR`, the error
functions, the identities, `NEXT VALUE FOR`) has the type the server fixes for it, and is nullable unless it is never NULL in
any state the server can be in. Each claim rests on an observation test. Where a statement stands never makes such a value
non-null: `ERROR_MESSAGE()` inside a `CATCH` and `SCOPE_IDENTITY()` straight after an `INSERT` are as nullable as anywhere else.

**Context.** Each of these was unknown (`unsupported-expression`), though the server fixes its type: `@@ROWCOUNT` is an `int`
wherever it is read. What varies is whether it can be NULL, and for some that depends on where it is read: `SCOPE_IDENTITY()`
is NULL until an insert in its scope gives an identity, and again after an insert into a table without one.

**Alternatives.**
- *Narrow by position*: non-null inside a `CATCH`, or after an `INSERT` into a table with an identity. That needs the walk to
  prove what ran last in the scope, on every path: an `INSERT` into a table without an identity makes `SCOPE_IDENTITY()` NULL
  again, and one of no rows leaves it as it was, NULL too if nothing before it gave one (observed). It would be a later
  change, with its own decision.
- *Leave them unknown.* That throws away a type the server fixes, which a reader can rely on, for the sake of the one fact
  that varies.

**How the IR says it.** A value that is NULL in some state takes the null rule `session-state`, with a detail saying which states
("NULL outside a CATCH"). That rule is new in IR 0.38, and a document is labelled 0.38 only when it holds one, so every other
document keeps its version. A value NULL for an argument the server can't find (`IDENT_CURRENT` of a missing table,
`USER_NAME` of an unknown id) keeps `out-of-range`, as `DB_NAME(id)` does. The new rule and the version are provisional: the
coordinator agreed them on 28 Nov 2026, for Richard to confirm.

**Reason.** A type is the part every state agrees on, so it can be claimed now; nullability that only some states rule out is
left as it is in the worst of them, which is sound wherever the value is read.

**What it costs.** `SELECT SCOPE_IDENTITY() AS id` after an insert is typed as possibly null, though in that place it rarely is,
so a client checks for a NULL it may never see.

**In the code:** the session rows of `Typer.FunctionTable.cs`, `Typer.Session.cs`, `IrNullRule.SessionState`; `FunctionTests.Session.cs`, `FunctionObservationTests.Session.cs`;
the rules in [the inference reference](../../src/Haruspex.SqlServer.Inference/README.md#session-values).
