# Fixture: haberdashery

`SELECT *` and `alias.*` expanded wherever a query stands: a procedure's rowset, `FOR JSON`, a CTE, a derived table, a set
operation, a temporary table and a table variable, an inline function, a scalar function and a write's `OUTPUT`. An invented haberdasher's shop
(ribbon, buttons and thread); no part of it comes from a real system. The rules are in the inference reference's
"SELECT *" section, observed in `SelectStarObservationTests`, and which tables a star is bound against is
[decision 0047](../../../docs/decisions/0047-a-star-is-bound-against-the-tables-the-project-defines.md).

| Procedure | Covers |
|---|---|
| `shop.order_card` | `SELECT *` before `FOR JSON PATH, WITHOUT_ARRAY_WRAPPER`: a key per column, `note` left out when null |
| `shop.list_orders` | A rowset of every column in declared order |
| `shop.order_lines` | `alias.*` on the optional side of a left join, after a listed column; an order with no lines |
| `shop.orders_with_lines` | A CTE of `SELECT *` read by `alias.*`, and a `FOR JSON` subquery of `alias.*` |
| `shop.current_prices` | A temporal table's star, without its `HIDDEN` period columns |
| `shop.list_parcels` | A star over sparse columns and their column set: the set, `xml`, in their place |
| `shop.picking` | `SELECT * INTO #t`, a star over it before and after `ALTER TABLE … ADD`, and a star over a table variable |
| `shop.order_summary` | A scalar function's `FOR JSON` object of `SELECT *`, and an inline function's `SELECT *` read by `alias.*` |
| `shop.all_lines` | A star in each query of a `UNION ALL` inside a derived table read by `alias.*` |
| `shop.reprice` | `inserted.*` and `deleted.*` in an `UPDATE`'s `OUTPUT`: without the period columns, and with the column set in place of the sparse columns |

The seed (`tests/fixtures/haberdashery/seed.sql`) sets every nullable column somewhere and leaves it null somewhere else; one
order has no lines and one parcel no sparse value, so its column set is NULL.
