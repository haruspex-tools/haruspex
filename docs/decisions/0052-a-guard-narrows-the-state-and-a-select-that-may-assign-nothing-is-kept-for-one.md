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

**Context.** Bodies check their inputs and their reads before using them, and haruspex threw the check away: after
`if @order_id is null return`, `select @order_id` was nullable, and after `select @name = p.name … where p.id = @id; if
@@rowcount = 0 return`, `@name` was nullable because the `SELECT` might have assigned nothing. Observed on SQL Server 2025
(`StatementFlowObservationTests.Guards.cs`): each guard form lets only a value past it in a call that succeeds; `RAISERROR`
of severity 10 lets the NULL through; a `CATCH` in the procedure swallows the `THROW` and the NULL reaches what follows; a
guard inside a loop holds on its pass only; any statement between a `SELECT` and the `IF` that tests `@@ROWCOUNT`, an `IF`'s
own test included, leaves the count other than the `SELECT`'s; a variable that held a value before the `SELECT` proves nothing
by holding one after it.

**Alternatives.**
- *Facts beside the state, as `Facts` holds a query's filters*, pushed at the guard and dropped at the next assignment. Every
  statement that can change a variable would have to know to drop them, and a loop's head or a `CATCH` to join them; kept in
  the value, the existing assignment and join do both, and nothing can forget.
- *A relational domain* ("`@name` is not NULL exactly when the `SELECT` read a row"), which would prove the same and more. It
  costs at every join (chapter 4 of the guide, "Alternatives"), and the two facts above are what bodies write; a
  `PossibleAssignment` is the one relation they need, kept only until it can change.
- *Path-sensitive walking*, keeping the paths of the `SELECT` apart until the guard. It doubles the states at each such
  `SELECT`, for a fact that two small sets carry.
- *Reading `@@ROWCOUNT` anywhere it is still the `SELECT`'s.* Which statements leave it alone is a list to observe statement
  by statement; straight after the `SELECT` is what bodies write, and anything else only claims less.

**Reason.** The principle asks for every fact the engine guarantees, and a guard is one: on the path that passes it the
condition was not TRUE, and in a variable nothing but an assignment can change that. Each fact is in the lattice the walk
already joins and iterates, so its soundness is the walk's: a narrowed value is below the value it narrows, an assignment
replaces it, and a join keeps only what every path holds.

**In the code:** `Walker.Narrowed`, `Typer.Proves` and `Typer.ProvesRows` (`Typer.Guards.cs`), `PossibleAssignment`,
`FlowState.Unassigned` and `FlowState.Possible`; the Inference README, "Guards"; the guide's chapter 6, "What a guard proves".
