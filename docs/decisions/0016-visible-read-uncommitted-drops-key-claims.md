# Visible read uncommitted drops key claims; the caller's isolation is assumed

**Date:** 10 Nov 2026

**Decision.** A source read with a `NOLOCK` or `READUNCOMMITTED` hint, or in a statement after the procedure itself sets
`READ UNCOMMITTED`, proves nothing by its keys: a lookup by key there may return more than one row. An isolation level set by
the *caller* is not visible, and haruspex assumes it is not read uncommitted. The documentation says so.

**Context.** Under read uncommitted a scan can return a row twice while pages split under it, so a key no longer proves "at
most one row". Microsoft documents this; it is not observed, since it needs another session splitting pages during the
scan. What is observed is how far a procedure's isolation level reaches
(`A_procedures_isolation_level_holds_in_the_procedures_it_calls_and_ends_when_it_returns`).

**Alternatives.**
- *Ignore the hint.* It would claim at most one row where the engine allows two.
- *Be strict about the caller too.* No source shows the caller's isolation level, so every key-based claim would go.

**Reason.** A hint in the source is visible, and dropping the claim it breaks only claims less. The caller's level is not, and
being strict about it would remove every cardinality claim haruspex makes. This differs from
[`COUNT`](0014-count-is-nullable.md), where being strict costs one `.nullable()`: the assumption is made only because the
cost of strictness is so much higher, and it is stated rather than hidden.

**In the code:** `BoundSource.Uncommitted`, set in `Binder`; `FlowState.ReadUncommitted`; the Inference README, "How many
rows"; `ReadUncommittedTests`, `ReadUncommittedObservationTests`.
