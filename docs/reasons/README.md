# Why a type is what it is

Every reason haruspex gives for a type is one of the IR's own codes: a **null rule**, why a value may be null or a result
missing; an **unknown reason**, why a value or a result can't be typed; or a **cardinality**, how many rows a result has. The
codes are the names the IR file writes (`nullableBecause`, `unknowns`, `cardinality`), so a tool that reads the IR reads them
here too ([decision 0079](../decisions/0079-a-surprise-is-explained-where-it-is-written.md)). Each page says what its code means
and names the test it rests on: for a rule of SQL Server's, an observation test that runs it against the server.
`ReasonCatalogueTests` fails for a code without a page, a short text or a test that exists.

The codes that start with HAR are something else: what a run reports, each with its page in [the diagnostics](../diagnostics/README.md).

## Where you meet them

- **In generated code.** By default (`"comments": "surprises"` in [the configuration](../configuration.md#comments-comments)),
  a field whose nullability, presence or type differs from what its source column declares, and every unknown, has a comment
  with each reason's short text and code, the way out when there is one, and the command that explains it. JSON Schema
  writes the same lines in the property's `description`. A key `FOR JSON` leaves out only because its column is nullable has
  none: the module's header says that rule once.
- **From `haruspex explain`**, which reads the committed IR and prints a field's derivation: the columns it is read from, each
  rule with where in the SQL it applied, and the page of each code ([the command line](../command-line.md#explain)).

For example, in the `shop` fixture's invented `shop.order_summary`, `max(l.shipped_at)` over a `LEFT JOIN shop.order_line AS l`
without `GROUP BY` may be null for three reasons, each shown with the line of SQL it comes from when the SQL is at hand:

```text
shop.order_summary last_shipped: datetime (local), datetime2(0), nullable
  from shop.order_line.shipped_at (datetime2(0)), declared at tables/order_line.sql:12:5
    | shipped_at  datetime2(0)  NULL,
  null nullable-column at shop.order_line.shipped_at: the column it reads is declared nullable
    | shipped_at  datetime2(0)  NULL,
  null unmatched-row at procedures/order_summary.sql:13:11: it comes from the optional side of an outer join, which has no row when nothing matches
    | LEFT JOIN shop.order_line AS l ON l.order_id = o.order_id
  null empty-aggregate at procedures/order_summary.sql:11:8: it is an aggregate without GROUP BY, which is null over no rows
    | max(l.shipped_at) AS last_shipped
  to change it: if the data rules it out, declare it in the config file with "nullable": false and why
  page nullable-column: https://haruspex.tools/reasons/nullable-column
  page unmatched-row: https://haruspex.tools/reasons/unmatched-row
  page empty-aggregate: https://haruspex.tools/reasons/empty-aggregate
```

## Null rules

Why a value may be null, or a result missing: the IR's `nullableBecause` and `absentBecause`.

| Code | Short text |
|---|---|
| [`nullable-column`](nullable-column.md) | the column it reads is declared nullable |
| [`unmatched-row`](unmatched-row.md) | it comes from the optional side of an outer join, which has no row when nothing matches |
| [`unmatched-lateral-row`](unmatched-lateral-row.md) | it comes from an `OUTER APPLY` whose query may return no row |
| [`empty-subquery`](empty-subquery.md) | it is a subquery, which is null when it returns no rows |
| [`all-fields-null`](all-fields-null.md) | it is a nested object left out when every one of its values is null |
| [`empty-aggregate`](empty-aggregate.md) | it is an aggregate without `GROUP BY`, which is null over no rows |
| [`function-result`](function-result.md) | it is a function's result, typed from its declared return type, which may be null |
| [`parameter`](parameter.md) | it is a parameter, which a caller may pass as null |
| [`null-input`](null-input.md) | it calls a function that returns null on null input with an argument that may be null |
| [`no-rows`](no-rows.md) | the query returns nothing when no rows match |
| [`not-run`](not-run.md) | the statement that returns it may not run |
| [`variable`](variable.md) | it is a variable that may not have been given a value that is never null |
| [`count-overflow`](count-overflow.md) | `COUNT` is an `int`, null past 2,147,483,647 rows when the caller's session has `ANSI_WARNINGS` and `ARITHABORT` off |
| [`arithmetic-overflow`](arithmetic-overflow.md) | arithmetic that overflows or divides by zero is null when the caller's session has `ANSI_WARNINGS` and `ARITHABORT` off |
| [`precision-loss`](precision-loss.md) | a decimal rounded to fewer places is null under a caller's `NUMERIC_ROUNDABORT` on with `ANSI_WARNINGS` and `ARITHABORT` off |
| [`failed-conversion`](failed-conversion.md) | `TRY_CAST` and `TRY_CONVERT` give null for a value that doesn't convert |
| [`subtotal-row`](subtotal-row.md) | `ROLLUP`, `CUBE` or `GROUPING SETS` add subtotal rows, which hold null in it |
| [`missing-for-action`](missing-for-action.md) | a `MERGE`'s `OUTPUT` has no value for it for some of its actions |
| [`no-branch-matched`](no-branch-matched.md) | a `CASE` without `ELSE` is null when no condition holds |
| [`null-literal`](null-literal.md) | it is, or may be, the `NULL` literal |
| [`out-of-range`](out-of-range.md) | a function given an index, code or position outside what it reads gives null |
| [`read-twice`](read-twice.md) | `COALESCE` reads its argument a second time, which may then be null |
| [`null-when-equal`](null-when-equal.md) | `NULLIF` is null when its two arguments are equal |
| [`json-null-or-absent`](json-null-or-absent.md) | it is read from JSON, which may hold null or nothing there |
| [`empty-frame`](empty-frame.md) | a window function over a frame that may hold no row is null there |
| [`window-edge`](window-edge.md) | `LAG` or `LEAD` without a default is null past the partition's edge |
| [`domain-error`](domain-error.md) | a math function outside its domain is null when the caller's session has `ANSI_WARNINGS` and `ARITHABORT` off |
| [`too-few-values`](too-few-values.md) | `STDEV` and `VAR` are null over fewer than two values |
| [`unknown`](unknown.md) | its type is unknown, so nothing is proved of it |
| [`declared`](declared.md) | the config file declares it may be |
| [`not-in-both`](not-in-both.md) | only one of the two databases returns it |

## Unknown reasons

Why a value or a result can't be typed: the IR's `unknowns` and `unknown` nodes.

| Code | Short text |
|---|---|
| [`unsupported-statement`](unsupported-statement.md) | a statement it runs is not modelled yet |
| [`external-code`](external-code.md) | it is made by code outside the project, such as a CLR procedure |
| [`redefined`](redefined.md) | the object is defined more than once, so which definition applies depends on order |
| [`unsupported-query`](unsupported-query.md) | a query form is not modelled yet |
| [`unsupported-expression`](unsupported-expression.md) | an expression or function is not modelled yet |
| [`unsupported-source`](unsupported-source.md) | a table source or join is not modelled yet |
| [`unsupported-sql-type`](unsupported-sql-type.md) | a SQL type is not modelled yet |
| [`unresolved-table`](unresolved-table.md) | a table it reads is not defined in the project |
| [`unresolved-column`](unresolved-column.md) | a column can't be found, or is ambiguous, in the tables in scope |
| [`unnamed-column`](unnamed-column.md) | a result column has no name |
| [`duplicate-column`](duplicate-column.md) | two result columns claim the same key |
| [`unresolved-recursion`](unresolved-recursion.md) | functions that call themselves reached no fixed point |
| [`json-shape-unknown`](json-shape-unknown.md) | the JSON's shape is in the data, not the code |
| [`caller-temp-table`](caller-temp-table.md) | it reads a temporary table its caller may create, whose shape depends on who calls |
| [`varying-results`](varying-results.md) | which result sets it returns depends on the path in a way a list can't say |
| [`dynamic-sql`](dynamic-sql.md) | it runs dynamic SQL, a string built when it runs |
| [`procedure-variable`](procedure-variable.md) | the procedure it calls is named in a variable |
| [`system-procedure`](system-procedure.md) | it calls a system procedure, whose results are not modelled |
| [`missing-procedure`](missing-procedure.md) | it calls a procedure the project doesn't define |
| [`ambiguous-procedure`](ambiguous-procedure.md) | the procedure it calls is named without a schema that decides which |
| [`trigger-results`](trigger-results.md) | a trigger it fires may return results of its own |
| [`hidden-column`](hidden-column.md) | the result has a column the client receives but can't name |
| [`encrypted-definition`](encrypted-definition.md) | the object is encrypted and its text is not in the source |
| [`sqlcmd-variable`](sqlcmd-variable.md) | it holds a SQLCMD variable, which a deployment replaces with its value |
| [`undecided-parameter`](undecided-parameter.md) | nothing in the query decides the parameter's type |

## Cardinalities

How many rows a result's query returns: the IR's `cardinality`.

| Code | Short text |
|---|---|
| [`many`](many.md) | any number of rows, none included: nothing bounds them |
| [`atMostOne`](atMostOne.md) | at most one row: `TOP (1)`, `FETCH NEXT 1`, or a filter that fixes a key |
| [`one`](one.md) | exactly one row: an aggregate without `GROUP BY`, or a query without `FROM` |
