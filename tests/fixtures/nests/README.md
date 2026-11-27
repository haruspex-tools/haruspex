# Fixture: nests

An invented shop (customers, orders, order lines, notes) whose procedures return `FOR JSON AUTO` and `ROOT` documents, so
that the differential tests hold each of `AUTO`'s nesting rules to a real server: which table's columns nest under which,
the key each array takes, where a column of no table goes, and what an outer join's unmatched row writes. The rules and the
observations they rest on are in [the inferencer's README](../../../src/Haruspex.SqlServer.Inference/README.md#for-json-auto-and-root).

The seed (`tests/fixtures/nests/seed.sql`) reaches every case the IR allows: an order with two lines and a note, one with a
line and no note, one with no lines (an outer join's line object with no keys), a line whose order is gone (a `RIGHT JOIN`'s
order object with no keys), and an order whose lines are apart when ordered by sku (the same order written twice). The
setups remove the orders or the lines, for the documents that are then absent or null.

| Procedure | Covers |
|---|---|
| `nest.orders_with_lines` | two tables: each order with an array of its lines under the lines' alias |
| `nest.orders_and_lines` | a `LEFT JOIN`: an order with no line has one line object of no keys; a column of no table after a line's column goes in the line |
| `nest.orders_and_lines_with_nulls` | the same with `INCLUDE_NULL_VALUES`: the line object's keys are null |
| `nest.customer_tree` | three levels; a column of the middle table after a line's column goes in the middle; a constant goes in the deepest object |
| `nest.lines_first` | the first table selected is outermost whatever the `FROM`'s order, and each table nests in the one selected before it, not the one it is joined to |
| `nest.scattered_orders` | parents whose rows are apart are written once for each run of them |
| `nest.unmatched_orders` | a `RIGHT JOIN`: an order object with its keys left out |
| `nest.named_sources` | sources without an alias, keyed by their names as the `FROM` writes them: a view and a function |
| `nest.other_sources` | a derived table over a CTE, a derived table and `VALUES` nest; `OPENJSON`'s column goes where it stands |
| `nest.dotted_keys` | a dotted alias is one key |
| `nest.one_order` | `WITHOUT_ARRAY_WRAPPER`: one order's adjacent rows merged into one object; no row when there is no line |
| `nest.rooted_orders` | `ROOT` over `PATH`, under the default key `root` |
| `nest.rooted_tree` | `ROOT('orders')` over `AUTO` |
| `nest.order_count` | `ROOT` over one aggregate row: always written |
| `nest.nested_documents` | `AUTO` and `ROOT` as subqueries: JSON with the wrapper, text without, `json_query` into a `ROOT` document, each null over no orders |
