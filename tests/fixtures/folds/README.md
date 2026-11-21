# Fixture: folds

An invented small shop for the expressions whose type depends on what SQL Server folds to a constant
([decision 0088](../../../docs/decisions/0088-a-type-that-depends-on-folding-is-typed-where-the-server-never-folds.md)). No
part of it comes from a real system. The seed (`tests/fixtures/folds/seed.sql`) gives a product with tags and a reference, one
with neither and no credits, the largest and smallest budgets `decimal(19,4)` holds and its smallest step; one run deletes
every tag, and another every product, so each `FOR JSON` result is seen absent.

| Procedure | Covers |
|---|---|
| `store.budget_report` | a view dividing a decimal by `NULLIF` of an applied derived table's `CASE` of literals (NULL for no credits), by a CTE's `CASE` over a count and by `NULLIF` of a column, and a multiplication by a derived table's literal past 38 digits: each counts its type's 10 digits, as the server does |
| `store.product_cards` | a view's `COALESCE` over a `json_arrayagg` subquery converted to `nvarchar(max)`, with `N'[]'`: JSON text in a rowset, escaped text in `FOR JSON`, the array again through `json_query` |
| `store.product_refs` | `CASE` on a table's column with no `ELSE` over `json_query(json_object(…))`, and `NULLIF` over a `WITHOUT_ARRAY_WRAPPER` subquery: escaped text in `FOR JSON`; and in `json_object` with a `COALESCE` over `json_arrayagg` whose fallback is `N'[]'` |
| `store.product_links` | a `UNION ALL` of `json_query` over `FOR JSON` subqueries: text in every row |
