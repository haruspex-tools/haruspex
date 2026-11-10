# Fixture: tabs

An invented small café for the common expressions, table-valued functions and visible read-uncommitted (slice 6f). No part
of it comes from a real system. The seed (`Seeds/tabs.sql`) has a guest without tabs, a tab without items, a note that is a
number and one that is not, and a menu item without a short code; every nullable column is null in some row returned.

| Function | Covers |
|---|---|
| `cafe.tab_totals` | inline, an ungrouped aggregate: one row for every tab; `COALESCE(SUM(qty * price), 0)` never null |
| `cafe.tab_by_id` | inline, a lookup by key: at most one row |
| `cafe.guest_tabs` | inline, many rows |
| `cafe.guest_open_tab` | inline, `TOP (1)` over a view (views are read inside functions since 6f) |
| `cafe.guest_label` | inline, built from its arguments only: a name never null stays so, a nickname may be null |
| `cafe.tab_lines` | multi-statement: its declared return table, with a `PRIMARY KEY`, `NOT NULL` and a `CHECK` |
| `cafe.open_tabs_json` | scalar, `FOR JSON` over a view |

| Procedure | Covers |
|---|---|
| `cafe.tab_summary` | `CROSS APPLY` of `tab_totals` on a lookup by key: at most one row; `COALESCE`, `ISNULL` with a literal, `CONVERT(decimal(19,2), …)` that may overflow, decimal subtraction, `CONVERT` of dates with styles 23 and 120 |
| `cafe.tab_receipt` | one JSON object: `CONCAT_WS`, `TRY_CAST` of a note (null when it is not a number), and in the items `CONCAT`, string `+` (null without a short code), `CAST` to `decimal(19,2)` that always fits |
| `cafe.guest_tab_list` | `guest_tabs`: many rows; `ISNULL(CONVERT(varchar(10), …, 23), 'open')` |
| `cafe.tab_lines_list` | `tab_lines`: its declared columns, many rows; one line by its declared key, at most one |
| `cafe.guests_with_open_tab` | `OUTER APPLY` of `guest_open_tab`: `outer-apply` for a guest without an open tab |
| `cafe.guest_labels` | `CROSS APPLY` of `guest_label` with a column never null and one that may be |
| `cafe.dirty_tab` | `WITH (NOLOCK)` on a lookup by key, then `READ UNCOMMITTED` before a function's lookup: many rows, not at most one |
| `cafe.open_tabs_payload` | `JSON_QUERY` of `open_tabs_json`: null when no tab is open |
| `cafe.guest_spending` | per guest, `CONVERT(decimal(19,2), COALESCE(SUM(…), 0))`, `MAX` of a left join's column, `COALESCE(MAX(tinyint), 0)` |
| `cafe.menu_prices` | a label built with `+` and `CONVERT(nvarchar(12), price)`, `price * 1.2` (numeric), `CAST(… AS int)` that may overflow, `ISNULL(char(4), '----')`, `CONCAT` with an `int`, `TRY_CAST` of a column never null (`failed-conversion`, seen both ways) |
