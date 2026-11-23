# A clause no rule reads makes the call unknown

**Date:** 23 Nov 2026

**Decision.** Every ScriptDom node type the typer accepts as an expression is listed in `AcceptedNodes`, with every property
the node has and how it is read: by the node's rule, as a clause, as `COLLATE`, or ignored with the reason. A clause of a
function call (`DISTINCT`, `OVER`, `WITHIN GROUP`, `IGNORE NULLS` or `RESPECT NULLS`, `TRIM`'s side, a JSON constructor's
pairs, null option, `ORDER BY` and `RETURNING`, `WITH ARRAY WRAPPER`) is read only by the rows of the function table that
say so (`FunctionTyping.Reads`), and a call carrying any other is unknown, with a reason naming the clause. A row reads a
clause only where an observation test it cites shows what the clause does. `OVER` and `WITHIN GROUP`, which are nodes
of their own, are listed the same way. A test compares the lists with ScriptDom's properties by reflection, both ways, and
fails on a node type ScriptDom derives from a listed one. `Typer.Expression` refuses a node of any type the list lacks before
it reads it, so the list is what the typer accepts, not a description of it.

Each clause is decided as follows (observed in `ClauseObservationTests`):

- **`DISTINCT`** is read by `COUNT`, `COUNT_BIG`, `SUM`, `AVG`, `MIN`, `MAX` and the four statistics, whose type and reasons to
  be NULL it doesn't change. `STDEV(DISTINCT x)` over two equal values is NULL, which `too-few-values` already says. `ALL` is the
  call without a clause. Any other call with `DISTINCT` is unknown.
- **`WITHIN GROUP`** is read by `STRING_AGG`, whose type and NULLs it doesn't change, and by the percentiles, which need it.
- **`TRIM`'s `LEADING`, `TRAILING` or `BOTH`** is read: the type is `TRIM(chars FROM s)`'s.
- **A JSON constructor's** pairs, null option and `RETURNING json` are read; so is `json_arrayagg`'s `ORDER BY`, which changes
  only the order. ScriptDom refuses `json_objectagg`'s `ORDER BY`, as the server does, and `RETURNING` anything but `json`.
- **`IGNORE NULLS` and `RESPECT NULLS`**, **`JSON_VALUE … RETURNING`** (which this server refuses, so its rule can't be
  observed), **`WITH ARRAY WRAPPER`** and **`WITHIN GROUP (GRAPH PATH)`** (refused with a percentile, 10753; not observed with
  `STRING_AGG`) are read by no row: a call with one is unknown.
- **A user's function** reads no clause.
- **`COLLATE`**, on any expression, keeps a Unicode string's value. Any other string keeps its type and its reasons to be NULL,
  and nothing else: converted to the collation's code page, `'é'` becomes `'e'` under `Greek_CI_AS`, and as `varchar(1)` is cut
  to `''` under a UTF-8 collation. `COLLATE` of anything else is refused by the server (447) and not modelled. A literal or
  parenthesis under `COLLATE` is not read as the literal it holds.

**Scope.** The lists cover the expressions the typer types and the clauses of a call. They don't cover a predicate (a
`BooleanExpression`: a `WHERE`, an `ON`, a `CASE` or `IIF` condition), which is read only to prove a value non-null, and which
treats a node it doesn't know as proving nothing; nor the nodes below a clause (an `ORDER BY`'s items, a frame's bounds).

**Context.** Audit finding F005: the function table checked a call's number of arguments and nothing else, so a property no
rule read was read as its default, and the call was typed as the call without it. `json_query(x WITH ARRAY WRAPPER)` was
typed as `x`'s object where the server returns an array (A5 fixed that one). The README says anything new is unknown until
modelled. A new version of ScriptDom that adds a property to a node the typer accepts would have broken that claim with no
test failing.

**Alternatives.**
- *Refuse every clause until each is modelled.* `COUNT(DISTINCT …)`, `STRING_AGG … WITHIN GROUP` and `COLLATE` are common, so
  this would make many columns unknown that the server's own description shows are typed as without them.
- *Check each clause in the method that types the row.* That is how the constructors did it, and it is what missed the
  array wrapper: a method that doesn't know a clause can't refuse it. A list read once, before any method, and held to ScriptDom
  by a test, can't miss one.
- *Treat `IGNORE NULLS` as making a value only less often NULL.* It looks sound, but it is a rule like any other and no row
  needs it yet (no vendored or fetched corpus uses it), so it is left to be observed and modelled when one does.

**Reason.** A claim about a call is about the call with all its clauses. Reading a clause as its default is a guess, and
the soundness principle refuses a guess. The reflection test turns a new property into a failing test, which someone must
answer by deciding how the typer reads it, rather than into a silent default.

**In the code:** `Clause.cs`, `AcceptedNodes.cs`, `FunctionTyping.Reads`, `Typer.Function` and `Typer.Collated`; tests in
`ClauseTableTests`, `FunctionTests.Clauses.cs` and `FunctionTableTests`, observations in `ClauseObservationTests`.
