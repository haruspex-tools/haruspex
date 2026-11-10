# Haruspex.SqlServer.Inference: statement flow and refinement

How a procedure's statements are read in order (slice 6), calls followed (slice 6b, "Calls" below), and what filters and
single-row queries prove (slice 6c, "Refinement" below). The code is `ProcedureAnalyzer.Flow.cs` (the walk),
`FlowState.cs` (the state and its join), `FlowLocals.cs` (what the binder sees), and `ShapeJoin.cs` (joins of values).
Every engine rule it leans on is observed on SQL Server 2025 RTM-CU10 and kept as a test in
`tests/Haruspex.DifferentialTests/StatementFlowObservationTests.cs`; the test names are given below.

## What the IR describes: calls that succeed

A path that raises an error to the caller describes no call that returns, so it is dropped. That is what makes
`IF @x IS NULL THROW …` a guard rather than a branch:

- `THROW` ends the procedure (`Throw_ends_the_procedure_and_the_call_fails`).
- `RAISERROR` of severity 11 or more goes on running, and later result sets are sent, but the client is handed the
  error, so the call fails (`Raiserror_of_severity_11_or_more_carries_on_but_the_call_fails`). So it ends the path. A
  severity of 10 or less is a message, and a severity that is not a literal may be either, so the path goes on.
- An error inside `TRY` goes to `CATCH` instead (`An_error_in_try_goes_to_catch_and_the_results_before_it_still_arrive`).
- A missing table is not caught by a `CATCH` in the same procedure (`A_missing_table_is_not_caught_by_catch_in_the_same_procedure`):
  it fails the call. Treating it as reaching `CATCH` anyway only adds states to the join, which claims less.

## The state

`FlowState` is immutable, so a branch starts from the state before it and both ends can be joined. It holds:

| Part | What it is | Starts as |
|---|---|---|
| Temporary tables, by name | `KnownTable` (columns, and what each may hold), `AbsentTable` (dropped here), `UnknownTable` (with a reason); a name not there is the caller's | empty: every `#t` is the caller's |
| Variables and parameters, by name | a `Value`: the declared type (with the type's facets, since every assignment converts to it), and why it may be null | a parameter may be null (`parameter`); a variable is NULL (`variable`) |
| Result sets | `ResultTrack`: for each position, what each path returned there; and how many results the paths have returned | none, one path at 0 |

Declarations are compiled, not run (`A_variable_or_table_variable_declared_in_a_branch_not_taken_still_exists`), so
variables' types and table variables are collected from the whole body before the walk, and a table variable is
always its declaration: nothing a statement does changes what its columns may hold.

### A temporary table's columns

- **Declared** (`CREATE TABLE #t`, `ALTER TABLE #t ADD`): the declaration is the guarantee. `NOT NULL`, the primary
  key and `IDENTITY` make a column never null; its `CHECK` constraints give facets, until `NOCHECK` or a dropped
  constraint withdraws them. A write never changes a declared column: the engine refuses a null where `NOT NULL` is
  declared, and converts every value to the declared type. A column of an alias type is unknown, because tempdb does
  not have the database's alias types (`A_temporary_table_cannot_use_the_databases_alias_types_but_a_table_variable_can`).
- **Made by `SELECT … INTO`**: each column holds what the select gave it, with its facets and origins (a column read
  unchanged keeps `nullable-column` at its base column, and its CHECK's facets). SQL Server decides such a column's
  nullability by its own reading of the select, which haruspex does not reproduce
  (`Select_into_a_column_from_a_not_null_column_refuses_null_but_others_take_it`; a column from a variable takes null
  whatever the variable holds, `Select_into_a_column_from_a_variable_takes_null`), so every later write is joined in:
  `ShapeJoin.Converted` keeps the column's SQL type (the write converts to it), keeps the facets both sides prove and
  at least the type's own, and unions the reasons to be null. A column an `INSERT` leaves out gets null.
- **JSON** stored in a column is text once stored (`Json_stored_in_a_temporary_table_is_text_but_json_through_a_cte_stays_json`),
  unless its SQL type is `json`, which is not tracked, so live JSON selected into a table is unknown.
- `DELETE` and `TRUNCATE` remove rows, which changes nothing a column may hold. `MERGE` into one, or an `ALTER TABLE`
  form not listed, makes it unknown.
- `CREATE TABLE #t` always makes a new table, hiding a caller's of that name
  (`A_temporary_table_a_procedure_creates_hides_the_callers_of_the_same_name`). A procedure that creates one name
  twice, even after a drop or in two branches, is refused when it is created (error 2714,
  `A_procedure_cannot_create_a_temporary_table_twice_even_after_dropping_it`), so re-creation never runs; it is
  modelled as the new table all the same.
- `ROLLBACK` undoes what was done to temporary tables in the transaction (`A_rollback_undoes_a_temporary_tables_creation`),
  which the caller may have begun, so after one every temporary table the procedure touched is unknown.
- Writes to permanent tables are not modelled (a trigger may return result sets), nor is an `OUTPUT` clause: each makes
  the results unknown (`unsupported-statement`), as any statement not listed does. `EXEC` and `INSERT … EXEC` into a
  temporary table or table variable are followed (below, "Calls").

## Joins, where paths meet

After `IF … ELSE`, at a loop's head, at `CATCH` and at the end of the procedure, the states of the paths that reach
the point are joined. Each join claims only what holds on every path:

| What | Join |
|---|---|
| A temporary table's column's declaration | kept only when it is the same on both paths; a column declared on some paths only, or differently, joins later writes like a `SELECT … INTO` column and is never read again from one path's declaration |
| A value (variable, column) | null if null on any path (the reasons of both); a scalar of the same SQL type keeps the facets both prove, each the looser of the two (longer maximum, lower minimum, the union of two sets); other shapes stay only when both are the same, or are unknown |
| A temporary table | the same table on both: column by column. Created on one path only: unknown, the caller's on the other (`caller-temp-table`). Dropped on one path only: unknown (`unresolved-table`) |
| Result sets | per position, everything either path returned there; the numbers of results so far, both |
| No path (every one ended) | the other side, unchanged |

- **`IF`** joins its two branches (no `ELSE` is a branch that does nothing). Conditions are not read.
- **`WHILE`** walks its body from the state at its head, which joins the state before the loop with the end of every
  pass and every `CONTINUE`, until a pass changes nothing (at most 20; each join only goes up). It ends at its head or
  at a `BREAK`. A body that returns a result set makes the results unknown (`varying-results`): how many it returns
  is not known.
- **`TRY … CATCH`**: `CATCH` starts from the join of the state before each statement inside `TRY`, at any depth, and
  the state after the last, since a result set cut short by an error still arrives with the rows sent before it
  (`A_result_cut_short_by_an_error_in_try_still_arrives_with_the_rows_sent_before_it`).
- **`RETURN`** ends a path successfully; its state joins the end of the procedure.

## Result sets are positional

A branch that is not taken returns no result set at all, so a result after it arrives at its position
(`A_branch_not_taken_returns_no_result_set_at_all_so_later_ones_move_up`). So the k-th result a client receives is
the k-th of its path, and `results[k]` is the union of every result some path returns k-th: one shape when they
agree, a `union` of the whole results when they differ (for a rowset a union of arrays, since every row of one result
set comes from one statement). A position some
successful path does not reach may be missing: its `absentBecause` has `not-run` at each statement that returns it
there. Only trailing positions can be, so the sets that arrive are the first ones listed. Results of different
encodings at one position, or a result inside a loop, make the results unknown (`varying-results`).

## CTEs

A CTE is part of its statement's query: its columns' values pass through unchanged, live JSON included
(`Json_stored_in_a_temporary_table_is_text_but_json_through_a_cte_stays_json`). The CTEs of one `WITH` are defined in
order, each seeing the ones before it; a CTE hides a table of its name read without a schema.

A **recursive CTE** is one anchor member and recursive members joined by `UNION ALL`. Its columns have exactly the
anchor's types: a recursive member of any other type, length included, is refused (error 240,
`A_recursive_member_of_another_type_than_the_anchor_is_an_error`), while nullability may differ
(`A_recursive_member_may_differ_from_the_anchor_in_nullability`). So the column keeps the anchor's SQL type, and only
nullability and the facets the members share need a fixed point: the recursive members are read with the CTE as it
stands, joined in by `ShapeJoin.Converted`, and read again until nothing changes (at most 20 passes). A recursive
member's column that is not modelled (`depth + 1`) is still of the anchor's type; only its nullability is unknown.

Not modelled yet: a CTE whose query is a `UNION` (other than a recursive one), several anchor members, `SELECT *`
inside one, and `WITH XMLNAMESPACES`.

## Calls

`EXEC` of a procedure of the project is followed (slice 6b): `ProcedureAnalyzer.Calls.cs` does a call, `ProcedureCalls.cs`
walks the callee and solves recursion. Every engine rule here is observed and kept as a test in
`tests/Haruspex.DifferentialTests/ExecObservationTests.cs`; the test names are given below.

### What a call does

- **Its result sets arrive where it is called**, in order, and a callee that returns none adds none
  (`A_callees_result_sets_arrive_in_the_callers_stream_where_it_is_called`). So the callee's results are placed in the
  caller's track at every position the caller's paths are at (`ResultTrack.Then`), exactly as a `SELECT` would be, and a
  call inside a branch or after a `RETURN` gets `not-run` like any result. `EXEC`, `EXECUTE`, `EXEC @rc = …`, named and
  positional arguments are all the same.
- **The callee is walked again at each call**, from the caller's temporary tables at that point (`CallContext`), so what it
  reads of them is the caller's (a callee read alone gives `caller-temp-table`), and its writes stay: `INSERT` and `UPDATE`
  widen a column as in the caller, `ALTER TABLE … ADD` adds one, `DROP TABLE` drops it
  (`A_callee_writes_its_callers_temporary_table_and_the_rows_stay`, `A_callee_can_alter_and_drop_its_callers_temporary_table`).
  A table the callee creates is gone when it returns (`A_temporary_table_a_callee_creates_is_gone_when_it_returns`); a callee
  that creates one of a name its caller has may have written the caller's before, which is not followed, so the caller's
  is unknown.
- **Its parameters are its declared types, nullable** (`parameter`), so its shape holds for every call; arguments are not
  used to specialise it.
- **The return code** (`EXEC @rc = …`) is never null after a call that succeeds: `RETURN NULL` gives 0
  (`The_return_code_is_never_null`). **An `OUTPUT` argument** holds what the callee set, or what was passed in when it set
  nothing (`An_output_parameter_the_callee_does_not_set_keeps_the_value_passed_in`), so it is its declared type and
  nullable (`parameter`). Neither changes result sets. The IR already marks output parameters; the return code is not
  recorded yet.
- **Errors.** A path of the callee that fails is a failed call, as in the caller. Inside a caller's `TRY`, an error anywhere
  in the callee (at any depth of calls) reaches the caller's `CATCH`: the callee is walked keeping each state an error may
  leave it from (`CallContext.Escapes`), and each becomes a state `CATCH` can start from, with the result sets sent so far.

### `INSERT … EXEC`

The rows of every result set the callee returns go into the table, and none reaches the client
(`Insert_exec_inserts_the_rows_of_every_result_set_and_returns_none_to_the_client`). A declared column is unchanged: each
value is converted to its type, a null fails a `NOT NULL` column and a longer string fails
(`Insert_exec_converts_each_value_to_the_columns_type_or_fails`). A column `SELECT … INTO` made joins with each value written
to it, as with `INSERT`. What a successful one never received is left out:

- a result set of another width than the insert's columns fails it, even with no rows (213,
  `Insert_exec_fails_on_a_result_set_with_the_wrong_number_of_columns_even_with_no_rows`);
- a FOR JSON query fails when it runs under `INSERT … EXEC` (13602), and one in a branch not taken does not
  (`Insert_exec_of_a_procedure_that_runs_for_json_fails`), so the callee is walked with `CallContext.Inserting`, under which
  that statement ends its path;
- `INSERT … EXEC` cannot be nested (8164, `Insert_exec_cannot_be_nested`), so under `Inserting` it ends its path too.

Without a column list, the rows go into every column but an identity column, which takes its next value. `SELECT … INTO`
copies a column's IDENTITY when it reads it unchanged from one table, and not through an expression
(`Select_into_copies_identity_and_insert_exec_without_a_column_list_skips_it`), so each column of a temporary table knows
whether it is one (`TempColumn.Identity`): a declared column from its declaration; a `SELECT … INTO` column is not one when
it isn't a plain column reference or reads a column that isn't, is one when it reads an identity column of the one table
in FROM, listed once, and may be one otherwise (a join, a CTE, a column listed twice). When a column may be one, which
columns an insert without a column list fills is not known, and the table becomes unknown.

`INSERT … EXEC` into a table variable works (`Insert_exec_into_a_table_variable_works`), and changes nothing it may hold.
Into a permanent table it is not followed (a trigger may return result sets).

### What stays unknown

`Binder.BindCall` (Semantics) says what an `EXEC` runs. Results that cannot be known make the caller's unknown, each with a
reason of its own, unless the config file declares them (below):

| Call | Reason |
|---|---|
| `EXEC (@sql)`, `EXEC (N'…')`, `sp_executesql` | `dynamic-sql` |
| `EXEC @name` | `procedure-variable` |
| A name starting `sp_` or `xp_` | `system-procedure`. SQL Server runs its own procedure of that name if it has one, whatever schema is written and whatever the database defines (`A_system_procedures_name_runs_the_system_procedure_whatever_the_project_defines`), and which it has is not known here, so even a procedure of the project named so is not followed. No catalogue of system procedures' shapes is built in |
| Another database, a linked server (`AT`), `OPENDATASOURCE`, a synonym, or no such procedure | `missing-procedure` |
| An unqualified name that more than one schema has | `ambiguous-procedure` |
| A CLR procedure | `external-code` |
| A procedure changed after its definition | `redefined` |

Under `INSERT … EXEC` such a call's result sets go into the table, so only the temporary tables become unknown: dynamic SQL
can create, write, alter or drop any of them (`Dynamic_sql_can_change_its_callers_temporary_tables`), and so can anything
else not followed. A callee's own reason reaches its caller, with the call named in the detail.

### Declared results

`IrInference.Infer` takes the results the config file declares for procedures (`DeclaredResults`, in the IR's terms;
Haruspex.Config reads them). A call whose results cannot be known, of a procedure that has a declaration, contributes the
declared result sets instead (`IrShapes` turns them back into shapes); what the callee does to temporary tables is still
not known, so they become unknown. The caller's IR lists the claim it now rests on, an override with `via`. A procedure's own
declaration is never applied here: the config layer applies it, and checks it against what is inferred.

### Recursion

A call of a procedure that is already being walked does not walk it again: it gives what is assumed of it, at first that
it never returns, then the result sets its last walk returned, until a walk returns what was assumed (at most 20 walks,
`ProcedureCalls.MostPasses`). The assumption must hold for the temporary tables the recursive call starts from, which the
procedure may have written before calling itself; so the walk starts from the join of the tables it was called with and
those of every recursive call met, and when a call widens them, it starts over from the wider tables. A recursive call both
under `INSERT … EXEC` and outside it is `unresolved-recursion`. Result sets only grow between walks, so this is the least fixed point, which describes every
call that returns, by induction on its depth. A procedure that returns more result sets at each depth never settles and is
`unresolved-recursion`. Every temporary table that the procedures the recursive call can reach write is unknown after it:
that part is not iterated. Calls nest at most 32 deep (error 217, `Calls_nest_at_most_32_deep`), which fails the call, so it
is a call the IR does not describe; the IR does not express the bound.

### Cost

A callee is walked at each call, and a call inside a loop at each of the loop's passes. The walk of a procedure from the
same temporary tables, with the same flags, is cached, unless it rested on an assumption about a recursive procedure below
it on the stack of calls.

## Expressions this needs

Two expressions were added because the shapes above are built from them: an integer literal within `int`'s range is
a non-null `int`, and `CAST`/`CONVERT` (without a style) of a string to a string type is of that type and null exactly
when its operand is (it cuts a longer string short without an error). A string type without a length is 30 long in a
`CAST` or `CONVERT`, and 1 in a declaration (`A_string_type_without_a_length_is_30_long_in_a_cast_and_1_in_a_declaration`). Arithmetic is not modelled: under a caller's
`ANSI_WARNINGS OFF` an overflow gives NULL rather than an error.

## Refinement

What a query's filters prove about the rows they keep (slice 6c): `Refinement.cs` (facts and three-valued logic) and
`Cardinality.cs` (how many rows). Every engine rule is observed on SQL Server 2025 RTM-CU10 and kept as a test in
`tests/Haruspex.DifferentialTests/RefinementObservationTests.cs`; the test names are given below.

### Which conditions prove a column non-null

`WHERE`, an inner join's `ON` and `HAVING` keep only rows for which their condition is TRUE. So a condition that cannot be
TRUE when a column is NULL proves it non-null in every row kept. `Possible` computes, for a column, the set of truth values
the condition can take when that column is NULL and everything else is anything:

| Form | When the column is NULL |
|---|---|
| a comparison (`=`, `<>`, `!=`, `<`, `>`, `<=`, `>=`, `!<`, `!>`) with the column as an operand, either side | UNKNOWN (`A_comparison_with_a_null_column_is_unknown_and_keeps_no_row`) |
| `LIKE` with the column as the value or the pattern | UNKNOWN (`Like_is_unknown_when_the_value_or_the_pattern_is_null`) |
| `BETWEEN`, as `x >= a AND x <= b`; `IN (list)`, as `x = a OR x = b …` | from those |
| `x IN (subquery)` | FALSE or UNKNOWN: FALSE when the subquery is empty (`In_a_subquery_with_no_rows_is_false_and_not_in_it_is_true_even_for_a_null`) |
| `IS NULL` / `IS NOT NULL` | TRUE / FALSE |
| anything else: the column inside `ISNULL`, `COALESCE`, `CASE` or any expression, `IS [NOT] DISTINCT FROM`, `EXISTS` | anything (`A_null_inside_isnull_coalesce_or_case_can_make_the_predicate_true`, `Is_distinct_from_is_never_unknown`) |

`AND`, `OR` and `NOT` combine the sets by three-valued logic, treating their operands as independent, which can only add
values. The column is proved non-null when TRUE is not in the set. This gives every rule the brief listed and nothing more:

- conjuncts at the top refine, each on its own (`UNKNOWN AND x` is never TRUE);
- under `OR`, only what every disjunct proves (`Or_keeps_a_null_when_any_disjunct_can_be_true`);
- `IS NULL`, `ISNULL()`, `COALESCE` and `CASE` never refine;
- **the catch-all filter `@x IS NULL OR c = @x` does not narrow `c`**: `@x IS NULL` can be TRUE whatever `c` is
  (`The_catch_all_filter_keeps_every_row_when_its_parameter_is_null`);
- `NOT` refines exactly where three-valued logic says so: `NOT (c = 1)` is UNKNOWN for a null `c`, so it refines, as do
  `NOT (c IS NULL)`, `NOT IN (list)`, `NOT BETWEEN` and `NOT LIKE`; `NOT IN (subquery)` is TRUE over an empty subquery, so it
  does not (`Not_of_an_unknown_comparison_is_unknown_and_keeps_no_null`). This is wider than "NOT never refines", and as
  sound, since it is the engine's own logic.

**ANSI_NULLS.** A procedure keeps the setting in force when it was created, and with it off `c = @x` is TRUE when both are
null (`A_procedure_created_with_ansi_nulls_off_keeps_a_null_that_equals_a_null_variable`). So under it `=` and `<>` against
anything but a column refine nothing; a comparison of two columns is not affected. Sources records `AnsiNullsOff` when a
`SET ANSI_NULLS OFF` (or `ANSI_DEFAULTS OFF`) precedes the definition in its file; otherwise it is assumed on, the default
of every client library and of SSDT. A `SET` in another file that runs first in the same session is not seen.

### Where a fact holds

A fact is recorded per query (`QueryFacts`) and holds while that query's select list is read, including the subqueries in
it, which see the outer row as it was filtered. A subquery's own filter never refines the outer query's columns. Sources are
compared by reference, since one table read twice is two sources.

- **A column fact** (`Values`): the column is non-null whenever its source's row is there. It removes `nullable-column`, or
  every reason of a CTE's, derived table's or temporary table's column (whatever made it nullable inside, the filter
  proves it is not), but not `outer-join`.
- **A row fact** (`Rows`): the source's row is there, not null-extended by an outer join. A null-extended row has every
  column NULL, so a filter that proves any of the source's columns non-null removes it; the source's columns lose
  `outer-join` and keep `nullable-column` (`A_where_condition_on_the_optional_side_of_an_outer_join_drops_the_rows_that_did_not_match`).
  It is the soundest model of "the outer join became an inner join for that side": only the source whose column is
  tested is proved there. In `a LEFT JOIN (b LEFT JOIN c …) …` a test of `b` says nothing of `c`, which shares `b`'s
  optional side in the binder's terms but has an outer join of its own.

| Condition | Column facts | Row facts |
|---|---|---|
| `WHERE`, `HAVING` | any source | yes |
| an inner join's `ON`, outside any optional side | any source | yes (`An_inner_join_after_an_outer_join_drops_its_unmatched_rows`) |
| an inner join's `ON`, inside an optional side | any source | no: the rows it kept may be null-extended around it |
| an outer join's `ON` | the optional side's only | no: it never removes a row of the kept side (`A_condition_in_the_on_of_an_outer_join_never_drops_a_row_of_the_kept_side`) |

**Two places where a filter proves nothing of the row returned:**

- A query that aggregates without `GROUP BY` returns its one row even when `WHERE` removed every row
  (`An_aggregate_subquery_returns_its_row_even_when_its_where_rejects_the_outer_column`). Its own columns are read only
  inside its aggregates, over the rows kept, so `WHERE` and `ON` facts about them hold; an enclosing query's column is read
  as it is, so no `WHERE` or `ON` fact about it is kept (nor a row fact). `HAVING` filters the one row, so its facts are
  kept. Whether a query aggregates errs towards yes: `HAVING`, a built-in aggregate in its select list or `ORDER BY`, or a
  call of a function the project does not show to be scalar (it may be a CLR aggregate).
- `ROLLUP`, `CUBE` and `GROUPING SETS` (and `WITH ROLLUP`, `WITH CUBE`) add super-aggregate rows in which the rolled-up
  columns are NULL (`Rollup_cube_and_grouping_sets_add_rows_whose_grouped_columns_are_null`). Every column of such a query's
  own sources read outside its aggregates gets `grouping-set`, which no filter removes, and the query returns many rows.
  This also corrects a claim older than refinement: a `NOT NULL` grouped column was non-null there.

`HAVING` also proves aggregates non-null: one compared there (`MAX(c) > 0`) is non-null in every group kept, matched in the
select list by its text. Refinement passes through CTEs, derived tables and `SELECT … INTO` tables with the values
themselves: what a query proved is what its row holds.

### Aggregates and scalar subqueries

- `COUNT` and `COUNT_BIG` are `int` and `bigint`, 0 over no rows. `COUNT` overflows past 2,147,483,647 rows, which is an
  error by default but NULL under a caller's `ANSI_WARNINGS` and `ARITHABORT` off
  (`Count_over_more_rows_than_int_holds_is_null_under_the_callers_ansi_warnings_and_arithabort_off`), and a procedure cannot
  prevent it, so `COUNT` is nullable with `count-overflow`, and `COUNT_BIG` never null. A `HAVING` that compares the count
  removes it.
- `MIN` and `MAX` return one of their argument's values: its type and facets, null when the argument can be, and
  `empty-aggregate` without `GROUP BY` (`An_aggregate_without_group_by_returns_exactly_one_row_even_over_no_rows`,
  `An_aggregate_with_group_by_returns_no_row_over_no_rows`). `SUM`, `AVG` and the rest are not modelled yet: they change
  the type, and `SUM` can overflow as `COUNT` does.
- A scalar subquery is its one column's value, with its type and facets, and `empty-subquery` unless its query returns
  exactly one row (`A_scalar_subquery_of_an_aggregate_without_group_by_always_has_a_value_for_count`). A `FOR JSON` subquery
  is NULL over no rows, so an array it builds has at least one item, and at most one where its query returns at most one.

### How many rows (`Cardinality.cs`)

| Query | Rows |
|---|---|
| an aggregate in the select list, or `HAVING`, without `GROUP BY` | one; at most one with `HAVING` (`Having_over_no_groups_returns_no_row_and_without_group_by_at_most_one`) |
| no `FROM` | one; at most one with `WHERE` |
| `GROUP BY`, even `GROUP BY ()` | many, and none over no rows (`An_aggregate_with_group_by_returns_no_row_over_no_rows`) |
| a filter fixing a key of every source, without `ROLLUP`, `CUBE` or `GROUPING SETS` | at most one |
| `TOP (0)` or `(1)` without `PERCENT` or `WITH TIES`; `FETCH NEXT 0` or `1` | at most one (`Top_1_and_fetch_1_give_at_most_one_row_but_with_ties_gives_more`) |

`TOP` and `OFFSET` never add rows; over a query of exactly one row they keep it only as a literal `TOP` of one or more, and
`OFFSET 0` (`Offset_keeps_the_one_row_of_an_aggregate_query_only_when_it_skips_none`). An aggregate counts for the query only when it is `COUNT(*)`, or every column its arguments read is of the
query's own sources: an aggregate of only an enclosing query's columns is that query's in SQL, and would not make the
subquery an aggregate query.

**Keys.** A source is fixed when every column of one of its enforced keys (`SchemaRules.EnforcedKeys`: the primary key,
unique constraints and unfiltered unique indexes of its definition, less any an `ALTER INDEX … DISABLE` anywhere may have
disabled: a filtered unique index allows duplicates outside its filter, `A_filtered_unique_index_allows_duplicates_outside_its_filter`,
and a disabled one allows them, `A_disabled_unique_index_allows_duplicates`) is compared with `=` to a value the same for every row: a literal, a variable, an enclosing query's column, or a
column of a source already fixed; or is tested `IS NULL`, since a key holds one NULL at most
(`A_unique_constraint_allows_one_null_only`). When every source is fixed, there is at most one row. A condition counts
from `WHERE`'s top-level conjuncts, or from a join's `ON` for the sources inside that join (the optional side, for an
outer join, which then matches at most one row per kept row); never under `OR`. Only tables are keyed.

The comparison must be the key's own equality: when SQL Server converts the key to the value's type, several rows can
match. `'5'` and `'05'` both equal 5 (`A_comparison_that_converts_the_key_can_match_several_rows`), and a `varchar` key
under a SQL collation compared with an `nvarchar` value is compared by Unicode rules, under which `'ss'` and `'ß'` are equal
(`A_varchar_key_under_a_sql_collation_compared_with_nvarchar_can_match_several_rows`). So the value must be of the key's
type, a narrower integer or decimal, a `varchar` for an `nvarchar` key, or a string for a key that is not a string, and
carry no `COLLATE` of its own (`InKeysType` lists them).

Not modelled yet: keys of temporary tables, table variables, CTEs and derived tables; `CREATE UNIQUE INDEX` (a separate
statement) and `ALTER TABLE … ADD CONSTRAINT … UNIQUE`; `SUM` and `AVG`; refinement of `UPDATE … FROM` and
`SELECT @v = …` by their filters. Each only claims less.
