# Fixture: bazaar

An invented weekend market for set operations, `COALESCE` and `CASE` over values of different types. No part of it
comes from a real system. Four kinds of sale each compute their columns their own way: a stall sale is priced per unit in
`decimal(9,2)` and dated by `date`; a web order is priced in `money`, placed at a `datetimeoffset(3)`, and carries a
`uniqueidentifier` voucher and its reference as text; an auction lot has a `decimal(38,10)` hammer price, a `decimal(30,0)`
reserve and a `datetime`; a barter has `bigint` units, a `float` estimate, a `smallmoney` fee, a `smalldatetime` and a
`time(7)`. The view `bazaar.all_sales` puts them in one shape with `UNION ALL`, so every column but the id takes its type by
precedence over four different ones.

The seed (`tests/fixtures/bazaar/seed.sql`) reaches the extremes the widened types must hold: `date`'s, `datetime`'s, `smalldatetime`'s and
`datetimeoffset`'s ends (each converts to `datetimeoffset(3)` unchanged), a hammer price of 28 nines and 10 places beside a
reserve of 30 nines (as `decimal(38,8)` it rounds to 10^28), `bigint`'s and `smallmoney`'s ends, a 200-character note and a
9,000-character memo. Each nullable column is NULL at least once. The default procedures cannot overflow on it, and
`bazaar.mixed` is also run under the caller's `ANSI_WARNINGS` and `ARITHABORT` off, where nothing may change.
`bazaar.lenient` runs only under those options: each of its columns reads only columns never null and is NULL on some rows
and not on others, so a missing `arithmetic-overflow` fails its run.

| Procedure | Covers |
|---|---|
| `bazaar.sales_feed` | the view as rows and as `FOR JSON`: `varchar(7)` kinds as a closed set of literals; `date`, `datetimeoffset(3)`, `datetime` and `smalldatetime` as `datetimeoffset(3)`; `smallint`, `int`, `tinyint` and `bigint` as `bigint`; `decimal(15,2)`, `money`, `decimal(38,10)` and `smallmoney` as `decimal(38,10)`; `varchar(10)`, `nvarchar(20)`, `char(4)` and `NULL` as `nvarchar(20)`; `varchar(200)` and `nvarchar(max)` as `nvarchar(max)`. Run on the seed and on an empty market |
| `bazaar.mixed` | a cut scale (`decimal(38,10)` beside `decimal(30,0)` is `decimal(38,8)`, rounding but never overflowing); `time(7)` beside `datetimeoffset(3)` (`datetimeoffset(7)`); a string beside a `uniqueidentifier`; `NULL` literals and typed `NULL`s; `INTERSECT` of `smallint` (a `CHECK` range of 1 to 100) and `bigint`, keeping the left side's range; `EXCEPT` of a `date` and a `datetimeoffset(3)`; a `decimal(3,2)` whose `CHECK` set is 0.25 and 0.75 beside a `decimal(38,0)` of 2 and 3 (the discounts round to 0 and 1, so neither set nor range is claimed); `COALESCE` of a `uniqueidentifier` and its text, never null; `CASE` of an `int` and `money`; `CASE` of a 38-digit literal and 2.5 (`numeric(38,0)`, 2.5 coming out as 3, so no literal set) |
| `bazaar.swap_tally_types` | columns declared without their arguments (`decimal`, `numeric`, `char`, `varchar`, `time`, `datetimeoffset`, `datetime2`, and `float(24)`, a `real`), read through `UNION ALL` and through `MIN` and `MAX` over a CTE's `UNION ALL`: each of the type its declaration's defaults give, as the server describes it. Run on the seed and on an empty tally |
| `bazaar.swap_tally_json` | the same columns through a derived table's `UNION ALL`, then `MIN` and `MAX` by group in `FOR JSON PATH`, whose keys take their types by the JSON path rather than the rowset's: each of the type its declaration's defaults give (`datetime2(7)`, not `datetime2`). Run on the seed and on an empty tally |
| `bazaar.lenient` | run only under the caller's options: `bigint` into `smallmoney` (3,000,000,000 and `bigint`'s ends), `float` into `datetime` (1e300), the text `'3000000000'` into `int`, `date` into `smalldatetime` (0001-01-01); each set operation read through a `CONVERT` to its own type, since read directly its overflow reaches SqlClient as a row it misreads (`SetTypeObservationTests`) |

Its `runs.json` doesn't set `seedReachesEveryEdge`: the computed columns cannot reach every edge of their types.
