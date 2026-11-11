# Recursion is solved as a fixed point

**Date:** 9 Nov 2026

**Decision.** A function or procedure that calls itself, directly or through others, is typed by iterating to the least
fixed point: start from "never returns", read the bodies again with the last step's results, and stop when nothing changes.
A recursive function's JSON becomes a named recursive type. What doesn't settle within a bound is unknown
(`unresolved-recursion`).

**Context.** Trees are common in schemas: categories, bills of materials, organisation charts, threaded comments. They are
usually built by a function that calls itself for each child.

**Alternatives.**
- *Report recursion as unknown.* Sound, and it would leave every tree untyped.
- *Unroll a fixed number of levels.* A type for depth k is wrong at k + 1, or claims a bound the data does not have. SQL
  Server's own limit, 32 nested calls, ends the call in an error rather than a value, so it is no bound on the shape.

**Reason.** The least fixed point describes every call that returns, by induction on its depth, and is the most precise of
the sound summaries the shapes can express (still an over-approximation, as every claim is). Naming the recursive node
keeps each step's shape finite, so the common cases settle in a few steps; the bound on steps, past which the result is
unknown, is what guarantees the iteration ends.

**See also:** [the guide, "Summaries and recursion"](../internals/07-summaries-and-recursion.md).
