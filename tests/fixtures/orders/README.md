# Fixture: orders

An invented order-management schema: customers, orders, order lines, products and salespeople. It
exists only to exercise the constructs haruspex models; no part of it comes from a real system.

Each procedure covers one group of constructs:

| Procedure | Covers |
|---|---|
| `sales.list_customers` | a plain rowset; aliases; declared `NULL` and `NOT NULL` |
| `sales.list_orders` | inner and left joins; a left join makes a `NOT NULL` column nullable |
| `sales.get_order` | `FOR JSON PATH, WITHOUT_ARRAY_WRAPPER`; dotted aliases; `json_query` over a correlated `FOR JSON` subquery; dates, times and GUIDs in JSON |
| `sales.list_customer_orders` | `FOR JSON PATH, INCLUDE_NULL_VALUES`; a bare correlated `FOR JSON` subquery |
| `sales.order_report` | expressions and references haruspex does not model yet (`upper`, a missing table and column, an unsupported type), each an unknown with a reason; `total * 1.2` is typed since slice 6f |
| `sales.archive_orders` | an `IF … THROW` guard: the path that throws is a failed call, which the IR does not describe, so the result always arrives (before slice 6, `IF` made the results unknown) |
