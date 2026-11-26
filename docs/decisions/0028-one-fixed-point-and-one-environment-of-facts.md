# One fixed-point iteration with one cap, and one immutable environment of what conditions prove

**Date:** 12 Nov 2026

**Decision.** Every solver in the inference (a `WHILE` loop, a recursive CTE, a recursive procedure, a component of
recursive functions) reaches its least fixed point through one Kleene iteration, `FixedPoint.Iterate`. It stops when a
pass gives back the state it started from, compared by structural equality; it stops at one cap for every solver,
`FixedPoint.MostPasses` (20 passes), past which the solver says unknown; and a solver's widening, where it has one (the
loop's, for JSON built from itself), applies from pass `FixedPoint.WidenAfter` (2) on.

What the filters of the queries being read and the conditions of the branches being read prove is one immutable
environment, `Facts`, extended on entering a query's select list or a branch and restored on leaving, with one set of
lookups. A filter's proof and a branch's are kept apart within it (`Facts.Proof`), since one holds where the row is there
and the other of the value itself.

**Context.** The four solvers each had their own loop, their own stopping test and between them three caps: 20 passes
for loops, CTEs and procedures, and 4 steps per function plus 4 for a component of functions. The proofs of filters and of
branches were two mutable lists of two classes, each with its own lookup for each question (whether a column is proved,
whether a row is there, whether an aggregate is), which every reader of a column had to combine in the right order.

**Alternatives.**
- *A cap sized to each solver*, as functions had: more steps for a bigger component. No component that big has been met,
  and a policy stated once is easier to check; the one cap grows for all if one is.
- *Widening with thresholds, or narrowing after widening*, to keep more where the cap is reached. The climbs met are of
  shape, not of number, and the cap is reached rarely enough that unknown there costs little.
- *A worklist*, re-reading only what changed. Bodies are small and passes few.
- *Facts as two stacks*, as they were. Two kinds of proof are one environment with a strength, and immutability makes
  leaving a query or a branch a matter of keeping the environment from before, not of popping the right list.

**Reason.** The argument that each solver is sound and ends is one argument (a state the step gives back is sound however it
was reached, a test that is never coarser than equality, a cap that jumps to the top; monotone steps from below make it
the least) and is now made once, in one place, and tested there
(`FixedPointTests`). One environment of facts, with one lookup per question, cannot be read half.

**Amended 22 Nov 2026.** The reason first said the argument was "monotone steps from below", which made a solver's
soundness depend on monotone steps (audit finding F051). A state the step gives back (or, for a widened loop, no more than
it) is sound however it was reached, by induction on the depth of nesting; monotone steps make the iteration climb, so it
can settle before the cap, and make what it settles on the least. The reason above says so.

**See also:** [the guide, "Flow, joins and fixed points"](../internals/06-flow-joins-and-fixed-points.md);
[decision 0012](0012-recursion-is-solved-as-a-fixed-point.md), on solving recursion as a fixed point.
