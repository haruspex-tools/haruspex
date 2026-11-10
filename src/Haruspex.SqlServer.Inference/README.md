# Haruspex.SqlServer.Inference: statement flow

How a procedure's statements are read in order (slice 6), and calls followed (slice 6b, "Calls" below). The code is `ProcedureAnalyzer.Flow.cs` (the walk),
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
`ProcedureCalls.MostPasses`). Result sets only grow between walks, so this is the least fixed point, which describes every
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
