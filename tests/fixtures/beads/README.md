# Fixture: beads

An invented bead shop whose procedures build JSON arrays as text, the idiom from before `json_arrayagg` and `json_array`:
`'[' + string_agg(…) + ']'`, `concat('[', …, ']')` and `'[' + concat_ws(',', …) + ']'`, read with `json_query`. Each element is
typed only where the server guarantees its text is one JSON value: a number converted to text, an escaped string in quotes,
a quoted literal, a JSON fragment (the README of `Haruspex.SqlServer.Inference`, "JSON assembled as text").

The seed (`tests/fixtures/beads/seed.sql`) reaches the cases the arrays allow: an order whose aggregates have items, among
them a tag holding a quotation mark and a backslash, which `string_escape` keeps one string; an order with none, whose
aggregates are NULL, so `coalesce(…, '')` and `concat` make `[]` and the quotes in `concat`'s literals make `[""]`; and every
subset of the channels.

| Procedure | Covers |
|---|---|
| `bead.order_card` | integers, decimals and bits aggregated into brackets with `+` and `coalesce`, and with `concat`; escaped strings each in quotes, and with the quotes in the separator; quoted literals joined by `concat_ws` in a function, read through a variable; JSON fragments joined |
| `bead.order_ids` | an array assembled as text returned as a column without `json_query`: a string |
