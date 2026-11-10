# Fixture: refine

An invented lending library for refinement (slice 6c): what filters, joins and single-row queries prove. No part of it
comes from a real system. Every column that may be null is null in some seeded row (`Seeds/refine.sql`), so each claim
of non-null is tested against rows that could break it.

| Procedure | Covers |
|---|---|
| `lend.search_titles` | the optional-filter search, `@x IS NULL OR c = @x` in either order: it must not narrow `author` or `genre` |
| `lend.authors_report` | a report filtering out nulls: `WHERE c IS NOT NULL`, and `HAVING MAX(c) IS NOT NULL` refining the aggregate; `MIN` of a column that is null for a whole group stays nullable |
| `lend.get_member` | a lookup by primary key as one JSON object (at most one), with `COUNT_BIG`, `COUNT` and `MAX` in scalar subqueries, and an outer join whose side may be left out |
| `lend.member_by_email` | a lookup by a unique key that is not the primary key: at most one row, the compared column never null |
| `lend.copy_at` | a lookup by a two-column key, with a second table joined by its key: still at most one row |
| `lend.open_loans` | a WHERE on the optional side of an outer join: the member's columns are no longer null for want of a match |
| `lend.branch_stats` | counts and a minimum in scalar subqueries over a CTE and a derived table whose filters pass through |
| `lend.overdue_members` | a filter's proof that survives `SELECT … INTO #t` |
| `lend.catalogue_summary` | aggregates without GROUP BY: exactly one row as a rowset and as JSON, over no rows too |
| `lend.latest_loan` | `TOP (1)`: at most one row, none for a member who never borrowed |
| `lend.copies_at` | a filter on part of a two-column key: any number of rows (two here) |
| `lend.loan_totals` | `ROLLUP` and `GROUPING SETS`: grouped columns null in the super-aggregate rows despite WHERE, and more than one row despite a key filter |
| `lend.member_digests` | aggregate subqueries returning the outer row's column that their WHERE tests: their one row comes even when WHERE kept none |
