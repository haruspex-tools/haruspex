# A type that depends on folding is typed where the server never folds

**Date:** 21 Nov 2026

**Decision.** Where a type depends on whether SQL Server folds part of an expression to a constant, haruspex types it only
where the server never folds that part, at every compatibility level from 130 to 170, with and without `OPTION (RECOMPILE)`,
and leaves it unknown elsewhere, saying what the server does. That covers two questions, each with its own rule:

- **An int's digits beside a decimal**, settled when the server derives the type. It folds only expressions over literals. A
  column of any source (a derived table's, a CTE's, a view's or a one-row `VALUES` list's literal too), a variable, a parameter
  and a subquery are never folded there, nor `ISNULL`, nor a `NULLIF`, `CASE`, `IIF` or `COALESCE` that reads one of those
  first. `OPTION (RECOMPILE)` changes nothing. So such an int counts its type's 10 digits. A derived table's literal counts
  its own only as a set operation's query as it stands, and `NULLIF` of two literals is folded in arithmetic only.
- **Whether a value nests as JSON or as escaped text**, settled with the plan. JSON built from a table's rows (a `FOR JSON` or
  `json_arrayagg` subquery, `json_query` of one, `json_object`), all of type `nvarchar(max)`, is never folded; JSON of a
  shorter type stays JSON through `COALESCE`, `CASE` and `NULLIF` whether folded or not. So `COALESCE` whose first
  argument is such JSON and whose last is a literal, and a `UNION` with such a query, are JSON text in every context. So is a
  `CASE` or `IIF` whose every condition compares a table's stored column with a literal, or tests it for NULL, with a result
  of `nvarchar(max)` JSON, when its last result is absent, NULL or a literal, and `NULLIF` over JSON text.

**Context.** On one application's corpus, 14 of 43 unknowns were these forms. Observing them (`FoldingObservationTests`)
showed the earlier model was also wrong in two places. It gave a derived table's literal its own digits inside arithmetic, and
`ISNULL` of a literal the literal's. Dividing by either gave too few places, a `multipleOf` that rejects real values. The
server's two foldings differ in reach. A derived table's `json_query(N'[1]')` is folded into a `COALESCE` over it, and a
parameter's value is folded under `OPTION (RECOMPILE)`, but neither is folded when the type is derived. In `json_object`, a
`CASE` or `COALESCE` that is never folded still nests as JSON when its last result is JSON it builds; in `FOR JSON` it is text.
So `COALESCE((SELECT … FOR JSON PATH), (SELECT … FOR JSON PATH))`, typed as text before, is unknown now.

**Alternatives.**
- *A wider type covering every folding, for example a decimal whose scale is the largest any folding gives, or a union of JSON
  and its text.* The decimal would hold every value, but the IR's type is the server's type (described in `check`, in the
  comments and in a cast the caller writes), so claiming one the server never gives misleads. The IR has no union of a live
  JSON value and its text. Neither was needed for the corpus's forms, which the rules above type exactly.
- *Model the plan's folding: a derived table's constant columns, a parameter under `OPTION (RECOMPILE)`.* It changes with the
  plan, and haruspex types only by rules that hold for every plan.
- *Model `json_object`'s own rule for a `CASE` or `COALESCE` (its last result).* Observed and stable, but a `CASE` nests differently in
  `FOR JSON` and in `json_object`, and a value's shape in the IR is one for every place it nests. Only the forms that are text in
  both are typed.

**Consequences.** `CHOOSE`, `NULLIF` over live JSON, `INTERSECT` and `EXCEPT` over JSON, a `UNION` of `json_query` of
constants, and conditions on a parameter, a derived table's column or a constant stay unknown, each with a reason that says
what the server does.
