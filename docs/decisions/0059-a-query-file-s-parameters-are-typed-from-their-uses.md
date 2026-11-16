# A query file's parameters are typed from their uses

**Date:** 16 Nov 2026

**Decision.** A query file's parameters are the variables its batch uses without declaring, in the order it first uses them.
Each takes the type its file declares in a comment, `-- @param @order_id int`, written as `DECLARE` would write the type; else
the type its uses decide, each by the rule SQL Server's own `sp_describe_undeclared_parameters` applies there, when every use
that decides one decides the same; else it is unknown, with the reason `undecided-parameter`, saying which uses disagreed or that
none decided. A use decides only with the parameter itself as an operand: compared with a column or a literal, in arithmetic
with one as a whole select-list column or `ORDER BY` key, a count of `TOP`, `OFFSET` or `FETCH`, assigned to a column by
`INSERT … VALUES` or `UPDATE … SET`, or passed to a procedure's or function's parameter. Each operand of a use is a use of its
own, so `@p IN (1, 2.5)` decides nothing unless both values give one type. Every parameter accepts NULL, as a procedure's does.
A query file is named from its file's name by the config file's top-level naming rules, having no schema; a name that clashes with
another object's, a procedure's or a query file's, is an error naming both, and neither is renamed, so that adding a file never
renames anything already generated.

**Context.** Many teams keep their queries in `.sql` files of the application rather than in procedures, and send them through
`sp_executesql`, which needs each parameter declared with a type. T-SQL gives a batch no place to declare its parameters, and a
`DECLARE` would make the name a local variable that a caller can't set. The server answers the question itself in
`sp_describe_undeclared_parameters`, so its answer is what to match.

**Alternatives.**
- *Ask the server.* `sp_describe_undeclared_parameters` needs a live database, which haruspex never reads
  ([decision 0001](0001-infer-from-source-never-a-live-database.md)). It also refuses a parameter used twice (error 11508),
  which rules out the common filter `c.customer_id = @id OR @id IS NULL`.
- *Model every rule the server has.* The server types a parameter inside an expression from the expression's context
  (`total = @x * 2` makes `@x` `decimal(38,19)`, from `total`), by rules that aren't documented. Where they aren't observed,
  a guess would be a claim the server may not keep; unknown with a reason is the honest answer, and a declaration settles it.
- *Declare parameters in the config file.* The type would live apart from the query, in another file, and drift from it.
- *A `DECLARE` the tool removes before sending.* The file would no longer run as it is in SSMS, which is half the point of a
  query file, and a reader would take the variable for a local.
- *Another form of comment*, such as a JSDoc-like block or a type in brackets after the name. One line per parameter, in SQL's
  own words for a type, reads like the `DECLARE` it stands for, needs no other syntax, and leaves room for more on the line later
  (a nullability, say) without changing what is there.

**Reason.** Where the server answers, haruspex gives the same answer, observed rule by rule; where it would differ, it says it
doesn't know, and the file declares the type. Uses that agree decide the type even where the server refuses to answer, because
each use alone has the answer the server gives, and the type is what the call declares: the walk of what runs is made with it,
so what is inferred of the results is true of every call. A type of a parameter is what the function checks a value against
before sending it, so a narrower type than the server's would refuse values the query takes: ordering widens a decimal, and
`LIKE` a string, as the server does, because a bound needn't fit the column it is compared with.

**In the code:** `ParameterRules` and `ParameterUses` (the rules, and where the uses are read), `QueryFiles` (the two walks) in
`src/Haruspex.SqlServer.Inference`; `QueryFileReader` (the file and its comments) in `src/Haruspex.SqlServer.Sources`. Each rule
is observed in `QueryParameterObservationTests`.
