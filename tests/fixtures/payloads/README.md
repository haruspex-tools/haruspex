# Fixture: payloads

An invented bookshop (authors, books, reviews, tags) whose procedures build their JSON with the constructors, the way
hand-written API procedures do: `json_query(json_object(…))`, with nested arrays and aggregates. Needs SQL Server 2025
(the aggregates and the native `json` type; `json_object` and `json_array` alone exist from 2022).

The seed (`tests/Haruspex.DifferentialTests/Seeds/payloads.sql`) reaches every edge: ids at `int`'s limits, every
string at its full length, prices, stars and weights at the bounds their CHECK constraints set, a null in every
nullable value, an author with no books, books with no reviews or tags, and groups of exactly one row (an aggregate's
fewest items). The facts of each column (lengths, digits, bounds) pass through the constructors unchanged, which is
what the edge check shows.

| Procedure | Covers |
|---|---|
| `pay.get_author` | a payload in `json_query(json_object(…))`: a key kept with null (`NULL ON NULL`), a native `json` column, a correlated `json_arrayagg` of objects with `ABSENT ON NULL` (null with no books), a `FOR JSON PATH` subquery inside the constructor; no row at all when nothing matches |
| `pay.list_books` | constructors as `FOR JSON PATH` columns: `json_array` as a tuple, as an array of the elements' types with its length bounds, and with `NULL ON NULL`; correlated aggregates in a `json_object` with `ABSENT ON NULL`; `TOP (1) json_object(…)` in a subquery; `json_objectagg` with data keys and nullable values; constructors nested in `json_query` |
| `pay.author_stats` | `json_arrayagg` and `json_objectagg` under `GROUP BY`, never null |
| `pay.reviews_by_book` | the same, of objects, and a record keyed by a number |
| `pay.author_documents` | a constructor and a `json` column as rowset columns: JSON text to parse |
| `pay.review_digest` | `json_arrayagg` and `json_objectagg` without `GROUP BY`, as rowset columns: `NULL` over no rows |
