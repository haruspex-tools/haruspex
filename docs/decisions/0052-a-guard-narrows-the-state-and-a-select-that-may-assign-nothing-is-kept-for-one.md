# A guard narrows the state, and a SELECT that may assign nothing is kept for one

**Date:** 16 Nov 2026

**Decision.** Each branch of an `IF` starts from the state before it, narrowed by what its condition proves there: a variable
or parameter the condition could not take that truth value with, were it NULL, is not NULL on that branch. When one branch
ends the path (`RETURN`, `THROW`, `RAISERROR` of severity 11 or more), the rest of the body starts from the other's, so after
`if @x is null return` the variable is never NULL. The fact is kept in the variable's value, and nowhere else.

Two facts are added to `FlowState` for what a guard can prove about a `SELECT @a = …, @b = … FROM …` that may read no row:

- `FlowState.Unassigned`: the declared variables no path has assigned yet, so each certainly NULL. Joined by intersection.
- `FlowState.Possible`: for each such `SELECT`, the value each of its variables holds if it reads a row
  (`PossibleAssignment`), its witnesses (those of its variables in `FlowState.Unassigned` before it), and whether
  `@@ROWCOUNT` still counts its rows. Joined by keeping only what both paths hold.

A guard that proves the `SELECT` read a row puts those values back: `@@ROWCOUNT` tested by the `IF` straight after it, or a
witness proved not NULL. An assignment to any of its variables ends it, and so does every statement but that `IF` for
`@@ROWCOUNT`.

**A row an existence guard found** (added the same day, after Richard chose it). `IF NOT EXISTS (SELECT … FROM t WHERE k =
@k) RETURN` leaves a `FoundRow` in `FlowState.Found`: the table, the guard's equalities, and whether the row is **held** for
later reads, which needs a transaction the body began and still has open, and either a lock the guard keeps (a hint of
`UPDLOCK`, `HOLDLOCK`, `SERIALIZABLE`, `REPEATABLEREAD` or `XLOCK`, or the `SERIALIZABLE` or `REPEATABLE READ` level) or the
transaction's snapshot (`SNAPSHOT` isolation, for a read at that level with no table hint). `FlowState.Transactions` and
`FlowState.Isolation` carry what that needs. A keyed read whose WHERE is only equalities the guard tested is one row when the
row is held; when it isn't, nothing is claimed and the reason a subquery of it may be NULL is the race, `read-twice` at the
guard, as for `COALESCE`'s second read. The fact ends at a write that may reach a permanent table, a call, a `TRUNCATE` or a
`ROLLBACK` (the body's own removal is no race), and at an assignment to a variable the guard compared with; the end of the
outermost transaction, on any path, makes it raced. A caller's transaction or isolation level is not seen: declaring them is
a later row.

**Context.** Bodies check their inputs and their reads before using them, and haruspex threw the check away: after
`if @order_id is null return`, `select @order_id` was nullable, and after `select @name = p.name … where p.id = @id; if
@@rowcount = 0 return`, `@name` was nullable because the `SELECT` might have assigned nothing. Observed on SQL Server 2025
(`StatementFlowObservationTests.Guards.cs`): each guard form lets only a value past it in a call that succeeds; `RAISERROR`
of severity 10 lets the NULL through; a `CATCH` in the procedure swallows the `THROW` and the NULL reaches what follows; a
guard inside a loop holds on its pass only; any statement between a `SELECT` and the `IF` that tests `@@ROWCOUNT`, an `IF`'s
own test included, leaves the count other than the `SELECT`'s; a variable that held a value before the `SELECT` proves nothing
by holding one after it.

For the existence guard, observed with a second session deleting the row between guard and read, each in a database of
its own (`StatementFlowObservationTests.ExistenceGuards.cs`): read committed, read committed snapshot, locking hints without a
transaction, a transaction without them, and snapshot isolation without a transaction all let the delete land, and the read
finds nothing; each locking hint and level above, inside a transaction, makes the delete wait and the read find the row, and
an inner `COMMIT` of nested transactions keeps the lock; `SNAPSHOT` in a transaction lets the delete go ahead and still reads
the row, unless the read uses `READCOMMITTEDLOCK` or the level is set back first.

**Alternatives.**
- *Facts beside the state, as `Facts` holds a query's filters*, pushed at the guard and dropped at the next assignment. Every
  statement that can change a variable would have to know to drop them, and a loop's head or a `CATCH` to join them; kept in
  the value, the existing assignment and join do both, and nothing can forget.
- *A relational domain* ("`@name` is not NULL exactly when the `SELECT` read a row"), which would prove the same and more. It
  costs at every join (chapter 4 of the guide, "Alternatives"), and the two facts above are what bodies write; a
  `PossibleAssignment` is the one relation they need, kept only until it can change.
- *Path-sensitive walking*, keeping the paths of the `SELECT` apart until the guard. It doubles the states at each such
  `SELECT`, for a fact that two small sets carry.
- *Claiming the row an existence guard found, as a variable's guard is claimed.* Another session can delete it between the
  two statements, as observed, so the claim would be false exactly when callers race; a held row is the case the engine
  guarantees.
- *Leaving the race unsaid*, `empty-subquery` as before. The race is a reason a reader can act on (add the hints, or a
  transaction), and `read-twice` already says it for `COALESCE`.
- *Reading `@@ROWCOUNT` anywhere it is still the `SELECT`'s.* Which statements leave it alone is a list to observe statement
  by statement; straight after the `SELECT` is what bodies write, and anything else only claims less.

**Reason.** The principle asks for every fact the engine guarantees, and a guard is one: on the path that passes it the
condition was not TRUE, and in a variable nothing but an assignment can change that. Each fact is in the lattice the walk
already joins and iterates, so its soundness is the walk's: a narrowed value is below the value it narrows, an assignment
replaces it, and a join keeps only what every path holds.

**In the code:** `Walker.Narrowed`, `Typer.Proves`, `Typer.ProvesRows` and `Typer.Finds` (`Typer.Guards.cs`),
`PossibleAssignment`, `FoundRow`, `Cardinality.Guard`, `FlowState.Unassigned`, `FlowState.Possible`, `FlowState.Found`,
`FlowState.Transactions` and `FlowState.Isolation`; the Inference README, "Guards"; the guide's chapter 6, "What a guard proves".
