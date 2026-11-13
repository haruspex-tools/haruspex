# Fixture: scopes

An invented lending library for binding: which table, CTE, derived table, temporary table or table variable each name in
a body means, and which query's source an unqualified column is. Each procedure is a scoping case SQL Server accepts, and
the seed (`tests/fixtures/scopes/seed.sql`) reaches every path, null-extended rows included, so the differential tests
check each claim against real rows. No part of it comes from a real system.

| Procedure | Covers |
|---|---|
| `lib.cte_hides_a_table` | a CTE hiding `dbo.shelf`, a table of its one-part name |
| `lib.cte_ends_with_its_statement` | the same, then the next statement's `shelf`, which is the table again |
| `lib.nested_ctes` | CTEs reading earlier ones, one read again from a scalar subquery and inside a derived table |
| `lib.cte_column_list` | a CTE's column list naming a column its query leaves unnamed |
| `lib.referral_chain` | a recursive CTE, read by unqualified columns |
| `lib.alias_reused_inside` | a derived table's alias reused for a table inside it |
| `lib.derived_alias_names_a_table` | a derived table named as a schema table is |
| `lib.correlated_two_levels` | a subquery's own subquery reading the outermost query's columns |
| `lib.unqualified_reaches_out` | an unqualified column the subquery's table lacks, found one level out |
| `lib.unqualified_two_levels_out` | one found two levels out, and one an inner table has, which does not go outward |
| `lib.sibling_subqueries` | two subqueries using one alias, each for its own table |
| `lib.exists_and_in` | correlated `EXISTS` and `IN` |
| `lib.apply_reads_its_left_side` | `CROSS APPLY` of a query reading its left input |
| `lib.apply_values` | `CROSS APPLY (VALUES …)` reading its left input |
| `lib.temp_table_from_a_branch` | a temporary table created in one branch of an `IF` and read after it |
| `lib.variable_and_temp_of_one_name` | `@tag` and `#tag`, two tables of different columns |
| `lib.shortlist_by_callee`, `lib.fill_shortlist` | a caller's temporary table filled by the procedure it calls |
| `lib.picks_from_a_call`, `lib.make_picks` | a temporary table created and read in a called procedure |
| `lib.order_by_an_alias` | `ORDER BY` a select-list alias |
| `lib.set_named_by_its_first_query` | a `UNION ALL` named by its first query |
| `lib.loans_through_a_view` | a view's column from a derived table inside it, outer-joined |
| `lib.lend` | `OUTPUT inserted.*` |
| `lib.merge_returns` | `MERGE … USING (query) AS s`, `OUTPUT` reading `$action`, `inserted`, `deleted` and `s` |
| `lib.update_returns`, `lib.delete_returns` | `UPDATE` and `DELETE` through a join, `OUTPUT` reading the joined table |
| `lib.update_through_a_cte` | an `UPDATE` through an updatable CTE, with `OUTPUT` |
