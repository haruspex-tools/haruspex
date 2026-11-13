# Binding is on demand, and kept for each set of names

**Date:** 13 Nov 2026

**Decision.** Semantics binds a body's names into an immutable tree (`QueryScope`, `BoundQuery`, a `RelationSymbol` for each
source that is not a table of the schema) and resolves each column and variable by one rule (`Binder.Resolve`). Binding is a
function of a fragment, the scope it is read inside and the names in force (`NameEnvironment`, compared by value), and each
result is kept for those three (`Bindings`): a query is bound the first time it is read with a given set of names, and never
again for the same set. The names in force include each temporary table's columns at the statement, which the walk passes
in. Inference attaches values to what binding gave (`ReadScope`) and resolves no name, which an architecture test reads its
compiled code for (`NameResolutionTests`). The walk's state keeps each variable's value under the name its resolution
declares, and each temporary table's under its name, which is its identity across a call.

**Context.** Binding was done as inference went: the binder called back into Inference for each derived table, view or call
to learn its columns, and Inference kept CTE frames and temporary tables in state it set before every statement. So binding
couldn't run alone, and a scoping rule (which source a name means) lived partly in each layer. The guide's chapter
[Binding](../internals/03-binding.md) gives the rules.

**Alternatives.**
- *One eager walk over each statement before inference.* A temporary table's columns at a statement depend on the path to it
  (SQL Server resolves `#t` when the statement runs), so an eager binder would need a flow analysis of its own, the walk's
  again, or would have to bind every statement once for every state that might reach it.
- *Binding again whenever a query is read.* Simple, and correct, since binding is a function; but a loop's body, a recursive
  CTE's members and both arms of a branch would be bound once a pass, and two readings of one query would get two trees, so
  what is known of a source (keyed by the source) couldn't be carried between them.
- *Temporary tables resolved by binding alone*, from their `CREATE TABLE`. A procedure creates a given `#t` once at most
  (observed), so which declaration a name means is static; but whether that `CREATE` ran, what an `ALTER TABLE` did and what
  a caller created are not, so the columns still come from the walk.

**Reason.** On demand, binding follows the order inference reads a body in (paths, passes, calls) without knowing it, and the
memo makes "once for each set of names" true by construction: equal inputs, the same tree. The names in force being an
argument, not a question asked of Inference, is what keeps binding a pure function over syntax and the model, usable by a
tool that never infers a value.

**See also:** [the guide, "Binding"](../internals/03-binding.md); decisions [0031](0031-the-model-is-closed-hierarchies-of-records.md) and
[0032](0032-a-switch-over-a-closed-hierarchy-is-exhaustive.md).
