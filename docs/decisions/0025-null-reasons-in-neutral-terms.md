# Reasons to be null are named for the reason, not the SQL

**Date:** 12 Nov 2026

**Decision.** Every reason a value can be null (`IrNullRule`) is named for why the value can be null, in terms any SQL
dialect shares, never for the T-SQL construct that causes it. Seven names that named a construct are renamed, and two of
them become one; the IR goes from 0.15 to 0.16. The construct stays findable: every reason's `at` is the column or the
source location it applies to, and the SQL Server front end's README lists the constructs that give each reason. The C#
members are renamed with them.

| 0.15 | 0.16 | C# member | Why the new name |
|---|---|---|---|
| `outer-join` | `unmatched-row` | `IrNullRule.UnmatchedRow` | the optional side of a join had no row to match |
| `outer-apply` | `unmatched-row` | `IrNullRule.UnmatchedRow` | the same reason, for a lateral join (SQL Server's `OUTER APPLY`) |
| `grouping-set` | `rolled-up` | `IrNullRule.RolledUp` | a row that totals over a grouped column holds null in it |
| `merge-action` | `missing-for-action` | `IrNullRule.MissingForAction` | a statement taking several actions reports a column that some of them don't set |
| `no-else` | `no-branch-matched` | `IrNullRule.NoBranchMatched` | a choice with no branch for when no condition holds |
| `nullif` | `nulled-when-equal` | `IrNullRule.NulledWhenEqual` | the value is made null when it equals a given one |
| `json-value` | `json-null-or-absent` | `IrNullRule.JsonNullOrAbsent` | the JSON holds null there, or (read leniently) nothing or the wrong kind of value |

The other 22 names already named a reason in shared terms and are unchanged: a nullable column, an empty subquery or
aggregate, a function's result or parameter, a variable, no rows, a statement not run, an overflow, a failed
conversion, a null literal, an index out of range, a value read twice, an empty window frame, a window's edge, a domain
error, too few values, all of an object's fields null, null input, an unknown, a declaration.

**Context.** The IR is meant to be dialect-neutral, so that a second front end (PostgreSQL's is on the roadmap) writes the
same format. A PostgreSQL front end would have had to say `outer-apply` for a `LEFT JOIN LATERAL` and `merge-action` for its
own `MERGE`, in SQL Server's vocabulary, or add a name of its own for the same reason, and a consumer would have had to know
both.

**Alternatives.**
- *Keep `outer-join` and `outer-apply` apart*, as `unmatched-row` and `unmatched-lateral-row`. They are one reason: the row
  a value comes from is missing because the optional side of a join matched nothing. A lateral join is a join whose right
  side may read the left; what makes a column null is the same. No consumer acts differently on the two, and a reader who
  wants to know which construct it was has `at`, which points at the join. One reason, one name.
- *Merge further*: `count-overflow`, `arithmetic-overflow` and `domain-error` all become null only when the caller's
  session turns an error into NULL. They stay apart because the remedy differs (`COUNT_BIG`, a wider type or a `COALESCE`,
  a check of the argument), and a person reading why a field is nullable is helped by the difference.
- *Keep the construct as detail in the reason*, a third property beside `rule` and `at`. It would add to every reason in
  every file for what `at` already locates, and make the format carry SQL Server's vocabulary after all, one level down.
- *Rename only the wire names and keep the C# members* (a member named for `OUTER APPLY` beside the name
  `unmatched-row`). The members are read in the inference beside the SQL they come from, where a construct's name
  would read naturally; but one vocabulary with two sets of names is the drift `IrWireNames` was made to stop, and the
  new names read as well in the code, since each says why.
- *Read 0.15 files with their names mapped*, since the table above makes that mapping mechanical. A reader of 0.16 still
  refuses 0.15 ([decision 0024](0024-an-ir-reader-refuses-other-versions.md)): this change is only renames, but a reader
  that mapped 0.15 would have to be kept, tested and trusted for every later version too, for files regenerated in one
  command. And a 0.15 file holds an older engine's claims, which the next version's mapping would label as its own.

**Reason.** The IR is a contract between any front end and any emitter. A name that belongs to one dialect makes every
other front end translate its own reasons into that dialect's terms, and every consumer learn them.

**See also:** [The IR as a contract](../internals/10-the-ir-as-a-contract.md), "Names that belong to no dialect";
[the IR's specification and changelog](../../src/Haruspex.Ir/README.md).
