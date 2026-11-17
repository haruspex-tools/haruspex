# Flow, joins and fixed points

A procedure's body is a program, and haruspex reads it the way a compiler's dataflow analysis does: statement by statement,
carrying a description of everything the body could hold at each point, joining descriptions where paths meet, and
iterating loops and recursion until the description stops changing. [Values as a lattice](05-values-as-a-lattice.md) is
about the descriptions; this chapter is about how the walk moves them through a body, and when it stops.

## The walk as a dataflow analysis

A **dataflow analysis** computes, for each point of a program, a fact that holds whenever execution reaches it. It needs
three things: a lattice of facts, a **transfer function** for each statement (the fact after it, from the fact before), and a
join for where control flow meets. Its result is sound when each transfer function and the join are, and it ends when the
lattice has no infinite climb, or when something stops the climb.

In haruspex the fact at a point is a `FlowState`:

| Part | Holds | Joined |
|---|---|---|
| `FlowState.Variables` | each variable's and parameter's value | value by value (`Value.Join`) |
| `FlowState.Temps` | each temporary table: its columns and what each holds, or why it is unknown | column by column (`TempColumn.Join`); a table on one path only is the caller's on the other, so unknown |
| `FlowState.Json` | what each traced `nvarchar(max)` place holds as JSON | slot by slot (`JsonSlot.Join`) |
| `FlowState.Results` | the result sets returned so far, by position | position by position, each the set of what any path returned there (`ResultTrack.Join`) |
| `FlowState.ReadUncommitted` | whether a statement may read uncommitted | either |
| `FlowState.Unassigned` | the declared variables no path has assigned yet, so certainly NULL | those on both |
| `FlowState.Possible` | what each `SELECT @v = …` that may read no row assigns if it reads one (`PossibleAssignment`) | those on both |
| `FlowState.Found` | each row an existence guard found, and whether it is held for later reads (`FoundRow`) | those on both, held only where both hold it |
| `FlowState.Transactions` | how many transactions the body has begun and not ended | the fewer |
| `FlowState.Isolation` | the isolation level the body set, if it set one | the level, where both agree |

Like a value, the state is a product of smaller lattices, and `FlowState.Join` joins it part by part. It is immutable, so
a branch starts from the state before it and the walk keeps both arms' states until they meet. A path that ends
(a `RETURN`, a `THROW`, a statement not modelled) has no state at all: `null`, the bottom, which joins to the other side
unchanged.

The transfer functions are the walk itself, `Walker.cs`: an assignment converts the value to the variable's
type and replaces it (or joins it, when the assignment may not happen); an `INSERT` joins what it writes into a column; a
`SELECT` appends a result set. Each statement's outcome is three states, not one (`Outcome`): where control goes next, and
what reaches a `BREAK` or a `CONTINUE`, which a loop joins in at its exit and its head.

Here the loop's body may or may not assign the note, and the server may stop the loop at any round:

```sql haruspex-example
CREATE TABLE shop.product
(
    product_id int          NOT NULL PRIMARY KEY,
    name       nvarchar(40) NOT NULL,
    stock      int          NOT NULL
);
GO
CREATE PROCEDURE shop.restock_note @product_id int
AS
DECLARE @note nvarchar(60) = N'unknown product';
DECLARE @round int = 0;
WHILE @round < 3
BEGIN
    IF EXISTS (SELECT 1 FROM shop.product AS p WHERE p.product_id = @product_id AND p.stock < 10)
        SELECT @note = p.name FROM shop.product AS p WHERE p.product_id = @product_id;
    SET @round = @round + 1;
END
SELECT @note AS note, @round AS rounds;
```

```ts haruspex-zod
export const RestockNoteRowSchema = z.object({
  note: z.string().max(60),
  rounds: z.int32().nullable(),
})
export type RestockNoteRow = z.infer<typeof RestockNoteRowSchema>

/** shop.restock_note: its parameters, checked before anything is sent. */
export const RestockNoteParamsSchema = z.strictObject({
  product_id: z.int32().nullable(),
})
export type RestockNoteParams = z.infer<typeof RestockNoteParamsSchema>

/** shop.restock_note: returns rows. */
export const RestockNoteSchema = z.array(RestockNoteRowSchema).length(1)
export type RestockNote = z.infer<typeof RestockNoteSchema>
```

`@note` is never null: on every path it holds either the literal it was declared with or a product's name, which is
`NOT NULL`, and the join of two non-null values is non-null. `@round + 1` may overflow, which is NULL under a caller's
options, so `rounds` may be null, whichever pass of the loop it came from.

## Where paths meet

Paths meet after an `IF`, after a `TRY` and its `CATCH`, and at a loop's head and exit.
At each, the walk joins the states that reach it with `FlowState.Join`, and only there: every other place that combines two
values is either the same join, one value at a time (a temporary table's column written twice, a recursive CTE's members,
the rows of a `VALUES` list), or the server's own type rule, which is not a join ([chapter 5](05-values-as-a-lattice.md),
"A knowledge join is not a type rule").

A join forgets which path a value came from. That is what makes the analysis finite, and it is also its main loss of
precision: after `IF @x IS NULL SET @y = 1 ELSE SET @y = NULL`, haruspex knows `@y` may be null, not that it is null exactly
when `@x` is not. Keeping the paths apart (*path-sensitive* analysis) would double the states at every branch.

## What conditions prove

One kind of knowledge doesn't flow from statement to statement but holds inside an expression, while a part of it is
read: what a condition proves. A `WHERE` keeps only the rows for which it is TRUE, so while the query's select list is
read, a column the condition can't be TRUE without is non-null; a `CASE` branch is read only when its `WHEN` was TRUE and
every earlier one was not. This is *refinement*, and Refinement.cs explains its three-valued logic.

What is proved is one immutable environment, `Facts`. Entering a query's select list extends it with what that query's
filters prove; entering a branch, with what the branch's conditions prove; leaving gives back the environment before. Its
lookups answer one question each: how strongly a column is proved non-null (`Facts.Proved`), whether a source's row is
there (`Facts.Matched`), whether a variable or an aggregate is proved, and why a column may be null in a rollup's
subtotal row (`Facts.Rolled`). The two kinds of proof are kept apart in the answer, because they mean different things:

- A **filter** proves a column non-null wherever its source's row is there (`Facts.Proof.WhereTheRowIsThere`). An outer
  join around the query may still null-extend the row, so the column keeps `unmatched-row`.
- A **branch** tested the value itself, in the same row after any join or grouping (`Facts.Proof.OfTheValue`), so a
  column it proves is non-null whatever else made it nullable.

Immutability is what makes the scoping right without care. A subquery inside a branch inside a query sees all three
frames, and when it is done, the walk is back to exactly the environment it had, because nothing changed it: it was
replaced, and the old one restored. The alternatives were lists pushed and popped, which is what there was: two of them,
one for filters and one for branches, with a lookup of each kind for each, which a reader had to know to combine.

## What a guard proves

A condition also proves something between statements. An `IF` takes its first branch only when its condition is TRUE, and
its other branch only when it is not, so each branch can start from the state before the `IF` *narrowed* by what its
condition proves there: the meet of each variable's value with "not NULL", where the condition shows it. The walker asks the
typer what the condition proves on each branch (`Typer.Proves`, the same `Facts` a `CASE` branch reads in) and narrows the
variables in the state it walks the branch from (`Walker.Narrowed`). When one branch ends the path, a `RETURN` or a `THROW`,
the join after the `IF` is the other branch's state alone: that is a **guard**.

```sql haruspex-example
CREATE TABLE shop.product
(
    product_id int          NOT NULL PRIMARY KEY,
    name       nvarchar(40) NOT NULL,
    note       nvarchar(60) NULL
);
GO
CREATE PROCEDURE shop.product_label @product_id int
AS
IF @product_id IS NULL
    THROW 50000, N'A product is needed.', 1;
DECLARE @name nvarchar(40), @note nvarchar(60);
SELECT @name = p.name, @note = p.note FROM shop.product AS p WHERE p.product_id = @product_id;
IF @@ROWCOUNT = 0
    RETURN;
SELECT @product_id AS product_id, @name AS name, @note AS note;
```

```ts haruspex-zod
export const ProductLabelRowSchema = z.object({
  product_id: z.int32(),
  name: z.string().max(40),
  note: z.string().max(60).nullable(),
})
export type ProductLabelRow = z.infer<typeof ProductLabelRowSchema>

/** shop.product_label: its parameters, checked before anything is sent. */
export const ProductLabelParamsSchema = z.strictObject({
  product_id: z.int32().nullable(),
})
export type ProductLabelParams = z.infer<typeof ProductLabelParamsSchema>

/** shop.product_label: returns rows, [] when it does not run. */
export const ProductLabelSchema = z.array(ProductLabelRowSchema).max(1)
export type ProductLabel = z.infer<typeof ProductLabelSchema>
```

The fact is kept in the value itself, not beside it, so nothing has to remember to undo it. It holds exactly as long as the
value: an assignment replaces the value, and a join with a path that didn't pass the guard (a loop's head after another pass,
a `CATCH` reached from before the guard) joins it away. The literature calls this *control sensitivity by assertions*, or
branch refinement: each edge out of a branch carries the fact its condition asserts there, applied to the state by the meet,
which keeps the analysis path-insensitive everywhere else.

A guard can also prove what a statement did. `SELECT @name = …, @note = … FROM … WHERE …` may read no row, and then assigns
nothing, so after it each variable is joined with what it held before. But every row it reads assigns every variable it
lists, so a guard that proves it read one proves all of them assigned. The state keeps what the `SELECT` assigns when it
reads a row, beside the joined values (`FlowState.Possible`), and two guards prove the read: `@@ROWCOUNT` tested straight
after it, and a test that one of its variables is not NULL when that variable was NULL on every path before it
(`FlowState.Unassigned`, a fact that holds only if it holds on every path, so its join is an intersection). Each ends when
anything could change it: an assignment to one of the variables, a statement that resets `@@ROWCOUNT`, or a join with a path
that does not hold it ([decision 0053](../decisions/0053-a-guard-narrows-the-state-and-a-select-that-may-assign-nothing-is-kept-for-one.md)).

In the example, `@product_id` is never NULL after the `THROW`; `@@ROWCOUNT` straight after the `SELECT` proves it read a row,
so `@name` holds a product's name, never NULL, and `@note` its note, NULL when the note is.

A guard can prove a row is there, too: `IF NOT EXISTS (SELECT … FROM shop.orders WHERE order_id = @order_id) RETURN`. But a
row, unlike a variable, belongs to every session, and the guard proves only that it was there when the guard ran. Under read
committed another session can delete it before the next statement reads it, and the read then finds nothing (observed, with
two sessions, in `StatementFlowObservationTests.ExistenceGuards.cs`). So the fact a guard leaves (`FoundRow`, in
`FlowState.Found`) says whether anything **holds** the row: a lock the guard keeps until the body's transaction ends (a locking
hint, or a `SERIALIZABLE` or `REPEATABLE READ` level), or that transaction's snapshot at `SNAPSHOT` isolation. Two more parts of
the state follow what that needs, the body's open transactions and its isolation level (`FlowState.Transactions`,
`FlowState.Isolation`). A keyed read of a held row returns exactly one row; one of a row nothing holds claims nothing, and
gives the race as its reason (`read-twice` at the guard). Each fact ends as the others do, and also at a write that may reach
a permanent table, a call or a rollback, after which the body itself may have removed the row; a `COMMIT` of the outermost
transaction leaves the row raced.

```sql haruspex-example
CREATE TABLE shop.orders
(
    order_id  int  NOT NULL PRIMARY KEY,
    placed_on date NOT NULL
);
GO
CREATE PROCEDURE shop.order_card @order_id int
AS
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM shop.orders WITH (UPDLOCK, HOLDLOCK) WHERE order_id = @order_id)
BEGIN
    COMMIT;
    RETURN;
END
SELECT (SELECT o.order_id, o.placed_on FROM shop.orders AS o WHERE o.order_id = @order_id
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS card;
COMMIT;
```

```ts haruspex-zod
// Generated by haruspex from IR 0.22. Do not edit.
// Rowset results follow the default type mapping of the mssql (tedious) driver.
import { z } from 'zod'

// JSON text: a string that must parse to a value of its content's schema. Its type stays string.
const jsonText = (content: () => z.ZodType): z.ZodString =>
  z.string().refine((text) => {
    try {
      return content().safeParse(JSON.parse(text)).success
    } catch {
      return false
    }
  }, { error: 'Expected JSON text holding a value of its content type' })

export const OrderCardCardSchema = z.object({
  order_id: z.int32(),
  placed_on: z.iso.date(),
})
export type OrderCardCard = z.infer<typeof OrderCardCardSchema>

export const OrderCardRowSchema = z.object({
  /** A string holding JSON, checked against the schema inside. */
  card: jsonText(() => OrderCardCardSchema),
})
export type OrderCardRow = z.infer<typeof OrderCardRowSchema>

/** shop.order_card: its parameters, checked before anything is sent. */
export const OrderCardParamsSchema = z.strictObject({
  order_id: z.int32().nullable(),
})
export type OrderCardParams = z.infer<typeof OrderCardParamsSchema>

/** shop.order_card: returns rows, [] when it does not run. */
export const OrderCardSchema = z.array(OrderCardRowSchema).max(1)
export type OrderCard = z.infer<typeof OrderCardSchema>
```

The guard holds its lock on the order until the `COMMIT`, so `card` is never NULL. Without `UPDLOCK, HOLDLOCK`, or without
the transaction, it would be nullable, `read-twice` at the guard.

## Fixed points

### One iteration for every solver

Four things in a body describe themselves: a `WHILE` loop's state depends on the state at its head, which depends on the
loop's last pass; a recursive CTE's rows on its own rows; a procedure that calls itself on what it returns; and functions
that call each other on each other's results. Each is a **fixed point**: a description that, fed back in, gives itself.

They are all solved the same way, by **Kleene iteration**: start from a description below the answer (a loop's state
before it, a CTE's anchor alone, a procedure assumed never to return, functions assumed to give nothing), apply the step,
and repeat until a pass gives back what it started from. When each step is monotone, the descriptions only climb, and the
first one that repeats is the **least fixed point**, the most precise sound answer
([chapter 7](07-summaries-and-recursion.md), "Solving recursion: the least fixed point", gives the argument).

There is one implementation of it, `FixedPoint.Iterate`, and each solver gives it a start and a step:

| Solver | Its state | Its start | Its step |
|---|---|---|---|
| A `WHILE` loop | the `FlowState` at its head | the state before the loop | walk the body, then join the state before the loop, the end of the body and every `CONTINUE` |
| A recursive CTE | its columns' values | the anchor's | read the recursive members with the CTE as it stands, and join their columns in |
| A recursive procedure | the result sets its calls of itself are assumed to return, and the tables its walk starts from | none, from its caller's tables | walk the body |
| Recursive functions | each function's result | no information (`NeverShape`) | read every body with the results assumed so far |

### When it stops

**The test.** A pass has settled when its result is equal, as a record, to its start (structural equality,
[decision 0022](../decisions/0022-values-compare-by-structure.md)). That is finer than "says the same": two states that
list the same reasons in another order are different records. A finer test may take a pass more, never stop a pass too
early, so it is safe; and every join keeps its left side's witnesses, so a state joined with a pass that adds nothing comes
back as the same record (`FixedPointTests` checks a state whose reasons come back in another order settles in two passes).

**The cap.** A lattice of shapes has climbs that never end: an array of arrays of arrays. So every solver stops after
`FixedPoint.MostPasses` passes, and one that hasn't settled says unknown, which claims nothing and so is always sound. A
cap is the coarsest **widening**: an operator that, where an iteration may climb for ever, jumps to an upper bound at which
it can stop. It is one number for every solver, stated once, because the reason for it is the same everywhere; the solvers
that settle do so in a few passes, well inside it.

**Widening before the cap.** One climb is common enough to deserve better than unknown for the whole loop: JSON built from
itself, `SET @x = (SELECT json_query(@x) AS c FOR JSON PATH)`, which nests one level deeper at every pass. A solver may give
a widening, which `FixedPoint.Iterate` applies from pass `FixedPoint.WidenAfter` on, so that what settles in a pass or two
keeps its precision. The loop's widening turns only the JSON that is still changing into unknown, naming the loop, and the
loop then settles with everything else it knows. What it settles on is then a fixed point above the least one: it allows
more than the least would, so it is still sound, but it is no longer the most precise answer the descriptions can give.
Without a widening, the iteration's answer is the least fixed point.

### Alternatives

- **A loop per solver**, which is what there was: four loops, three caps (20 passes, and 4 per function plus 4 for a
  component of functions), and four ways of saying "settled". Each was right; together they were a policy nobody could
  state in one sentence.
- **A cap per solver, sized to it.** A component of functions was given more steps the more functions it had. The bigger
  components have not been met, and one stated cap is easier to trust; if one is, the cap can grow for all.
- **Widening with thresholds**, jumping a growing number to the next of a few landmarks rather than to the top. Nothing
  here climbs by number: the climbs are of shape, and of reasons, which are finite.
- **Narrowing**, a second iteration downwards from the widened result to win precision back. It pays where widening is
  coarse and frequent; here it happens only at the cap and in the loop's JSON.
- **A worklist** (*chaotic iteration*), re-reading only what changed. The bodies are small, and the passes few.

## Reading further

- Anders Møller and Michael I. Schwartzbach, *Static Program Analysis*, chapters 5 and 6: dataflow analysis, the
  fixed-point algorithms, and widening and narrowing.
- Anders Møller and Michael I. Schwartzbach, *Static Program Analysis*, the chapter on path sensitivity and relational
  analysis, "Control sensitivity using assertions": a branch's condition refining the state on each of its edges, which
  "What a guard proves" applies.
- Patrick Cousot and Radhia Cousot, "Abstract interpretation: a unified lattice model for static analysis of programs by
  construction or approximation of fixpoints" (POPL 1977): where Kleene iteration and widening come from.
