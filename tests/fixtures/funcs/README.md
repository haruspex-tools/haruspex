# Fixture: funcs

An invented small gym for the built-in functions: date and string functions, `STRING_AGG`, `JSON_VALUE`, window
functions, arithmetic and precedence over `float`, `real`, `money`, `smallmoney`, `datetime` and `smalldatetime`, the bare `NULL`
and `GENERATE_SERIES`. No part of it comes from a real system. The seed (`tests/fixtures/funcs/seed.sql`) makes each nullable input NULL at
least once, gives the plus plan only NULL nicknames, gives one member only untimed visits, and leaves every framed window and
`LAG`/`LEAD` empty at a member's first or last visit.

A bare `NULL` select item is not here: it is always NULL, which the IR cannot say yet (it is `int`, nullable for `null-literal`),
and the seed check wants every nullable field seen with a value. `FunctionTests` and `FunctionObservationTests` cover it.

| Procedure | Covers |
|---|---|
| `gym.member_dates` | `DATEDIFF`, `DATEDIFF_BIG` (also over `0`), `DATEADD` over `date`, `datetime`, `smalldatetime` and a string, with a NULL number; `DATEPART`, `YEAR`, `MONTH`, `DAY`, `DATENAME`; `EOMONTH` with a NULL offset (never null); `CAST(NULL AS date)` in an `IIF` branch some rows take; `DATEFROMPARTS` with a NULL part, `DATETIME2FROMPARTS`, `TIMEFROMPARTS`; the current date and time functions and `CURRENT_TIMESTAMP` |
| `gym.member_names` | trimming and case over `char`, `nvarchar` and a `CHECK`ed column (its set dropped); `LEFT`, `RIGHT`, `SUBSTRING` past the end and over `max`; `LEN`, `DATALENGTH`; `REPLACE`; `STRING_ESCAPE`; `FORMATMESSAGE` with a NULL argument, and with a format parameter (a run passes NULL); `JSON_VALUE` over a string, a JSON null, an object, a missing document and a long string; the same nested in `FOR JSON PATH` (null keys left out) |
| `gym.visit_totals` | `STRING_AGG` grouped (a group of only NULLs), with `WITHIN GROUP`, over an integer, and ungrouped over a member's visits (a member with only NULLs, and none; over a column never null, NULL only for no rows) |
| `gym.visit_windows` | ranking; `COUNT`, `COUNT_BIG`; `SUM` over a partition, over an empty `ROWS` frame (NULL over a never-null column) and running; `MAX` over a frame after the current row; `MIN` over a partition; `MAX` and `FIRST_VALUE` of a nullable column (NULL
throughout member 2's visits); `LAG` and `LEAD` without a default (`window-edge`), with one, and offset 0; `LAG` in a `CASE` branch that tests the current row's value, NULL where the row before is untimed; `FIRST_VALUE`, `LAST_VALUE` over an empty frame; `PERCENTILE_CONT` and `PERCENTILE_DISC` over a partition of only NULLs |
| `gym.visit_money` | `money` and `smallmoney` with integers, a decimal and a literal; `float` and `real`; unary minus over `money`, `float` and `tinyint` (`smallint`); `float` and `money` literals; `datetime` and `smalldatetime` arithmetic; `COALESCE` over `real` and `float`, `smallmoney` and `tinyint`, `datetime` and `datetime2(3)`, `smalldatetime` and `datetime` |
| `gym.lenient` | run only under the caller's `ANSI_WARNINGS` and `ARITHABORT` off (the run's setup sets them): a `LAG` default that overflows `tinyint` past the edge, and `float`, `money` and `datetime` arithmetic that overflows on some rows, each NULL there (`arithmetic-overflow`) |
| `gym.series` | `GENERATE_SERIES` up to a parameter (NULL: no rows), applied per visit over `tinyint`, and over decimals |
