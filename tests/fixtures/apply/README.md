# Fixture: apply

An invented small market for `CROSS APPLY`, `OUTER APPLY`, `VALUES` lists and views (slice 6e). No part of it comes from a
real system. The seed (`Seeds/apply.sql`) has outer rows an APPLY finds nothing for: a customer without purchases (and
without an email or city), a purchase without lines, a sent purchase that the open-purchase view leaves out, and a price
changed once (in a later batch, so its history row has a period); so every
claim about an APPLY's right side meets data that could break it. Every nullable column is null in some row returned.

| Procedure | Covers |
|---|---|
| `mart.list_purchases` | the nested-payload idiom: `OUTER APPLY (SELECT JSON_QUERY((… FOR JSON PATH)) AS lines)`, one row for every purchase, `lines` null only for a purchase without lines (`empty-subquery`, never `outer-apply`) |
| `mart.purchases_json` | the same through `CROSS APPLY` inside `FOR JSON PATH`, the lines live JSON and left out when empty |
| `mart.customer_tree` | two levels of `OUTER APPLY`, customers → purchases → lines, as one JSON document |
| `mart.get_purchase` | a lookup by key with a `CROSS APPLY` of an ungrouped aggregate (never null, `MAX` an empty aggregate) and an `OUTER APPLY` of `TOP (1)`: at most one row |
| `mart.customer_purchases` | `CROSS APPLY` of a query that may have no row: customers without purchases are dropped, the right side keeps its own nullability |
| `mart.last_purchase` | `OUTER APPLY` of `TOP (1)`: the right side's columns `outer-apply` |
| `mart.customer_activity` | `OUTER APPLY` of an ungrouped aggregate whose `WHERE` tests the outer column it returns (still nullable); an outer `WHERE` on the right side's column proving it |
| `mart.product_sizes` | `VALUES`: one row naming two columns, and an unpivot of two `int` columns labelled by string literals |
| `mart.purchase_lines` | a view joining a purchase to its lines, filtered on the purchase's key: many rows, no key passes through |
| `mart.price_versions` | a system-versioned table read `FOR SYSTEM_TIME ALL` directly, through a view, and on a keyed view: a key filter gives every version; and a view of one row by its own key filter read `ALL`, directly and through `CROSS APPLY` |
| `mart.open_purchase_by_id` | a schema-bound, indexed view over one table with its whole key, read `WITH (NOEXPAND)`: at most one row by that key |
| `mart.open_contacts` | a view over two views, one with a column list |
| `mart.list_products` | a view whose query is `SELECT *` |
| `mart.purchase_overview` | a view with a join, a `CROSS APPLY` of an aggregate and an `OUTER APPLY` of `TOP (1)` inside it |
