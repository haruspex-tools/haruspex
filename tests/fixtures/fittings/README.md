# Fixture: fittings

An invented tailor's customers, for masked columns: every column but the key and the name is declared `MASKED WITH (FUNCTION =
…)`, one of each function a type takes (`default()`, `email()`, `partial()`, `random()`, `datetime()`), most with a CHECK
constraint or a fixed length that the mask breaks. No part of it comes from a real system. The differential tests read every
run as a user without `UNMASK`, so what they hold the IR to is each mask; the seed (`tests/fixtures/fittings/seed.sql`) has a
customer at each CHECK's lower edge, one at its upper edge, and one with every nullable column NULL and an empty string where one
is allowed, none of which the masks show. It has no config file, so haruspex claims what a caller without `UNMASK`
reads ([decision 0118](../../../docs/decisions/0118-a-masked-column-claims-only-its-type-nullability-and-greatest-length.md)).

| Procedure | Covers |
|---|---|
| `tailor.customer_card` | Each masked column read as it is: its type, whether it may be null and its greatest length, and no CHECK's range or set, fixed length or minimum length |
| `tailor.customer_summary` | Values computed from masked columns (`+`, concatenation, `CAST`, `ISNULL`, `LEN`, a `CASE` that may give one, `COUNT` over a window, a scalar function that reads one), each masked as a whole; and a `CASE` whose condition reads one, which isn't |
| `tailor.bands` | A `UNION ALL` of a masked column and a literal, masked in every row |
| `tailor.contacts` | A view's and a derived table's columns, masked as what they read is |
| `tailor.customer_json` | `FOR JSON` over masked columns: each value its mask, the JSON's shape its own |
| `tailor.customer_sizes` | `FOR JSON` nesting JSON computed from a masked column, which the server writes as its mask without quotes: its results can't be listed |
