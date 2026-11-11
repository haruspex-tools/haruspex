# Fixture: ledger

An invented small ledger for decimal arithmetic, `AVG`, the statistical aggregates and the numeric functions. No
part of it comes from a real system. `ledger.line` has a column of each numeric type the rules read, `decimal(38,0)` and
`decimal(38,38)` among them. The seed (`tests/fixtures/ledger/seed.sql`) puts one line at every column's greatest value and one at every
least value (`real` within 1e31 of its ends), makes each nullable input NULL at least once, gives one account a single line
(so its sample statistics are NULL), one account lines whose rates, discounts, scores and deltas are all NULL, and one account
none. A line has a price of 0 and no units, another a negative price.

The default procedures hold only expressions that cannot overflow on the seed, and two of them are also run under the
caller's `ANSI_WARNINGS` and `ARITHABORT` off, where nothing may change. `ledger.lenient` runs only under those options: each
of its columns reads only columns never null and is NULL on some lines and not on others, so a missing `arithmetic-overflow`
or `domain-error` fails its run.

| Procedure | Covers |
|---|---|
| `ledger.line_values` | decimal `*` within and past 38 digits (`decimal(19,2) * decimal(19,6)` is `decimal(38,7)` and cannot overflow at the extremes), `-1.0 * decimal(19,2)`, `decimal(38,38)` squared (rounds to 1), division by integer and decimal literals and by a column (`arithmetic-overflow`), `%` over decimals and integers, a `bit` beside a decimal, `money / 4`, unary minus over `decimal(38,0)` |
| `ledger.line_functions` | `ROUND` keeping its places, truncating, over `tinyint`, `real`, `smallmoney` and `money` to 2 places (never NULL at money's ends); `FLOOR` and `CEILING` over a decimal, `decimal(38,38)`, `smallmoney`, `real`; `ABS` over `smallint`, `decimal(38,0)`, `smallmoney`; `SIGN` over `decimal(38,38)` (`decimal(38,37)`), `money`, `int`, `numeric`; `POWER` with whole exponents that fit; `SQRT`, `SQUARE`, `EXP`, `LOG` (both forms) and `LOG10` where they cannot fail, and `SQRT(ABS(real))` (`domain-error`, never reached) |
| `ledger.account_stats` | `AVG` over each type, `AVG(DISTINCT)` over `decimal(38,38)`; `STDEV`, `STDEVP`, `VAR`, `VARP` grouped (a group of one line: `too-few-values`; groups of only NULLs); then over one account without `GROUP BY` (an account without lines: `empty-aggregate`), run for an account of three lines, of one, of none, and an empty ledger |
| `ledger.line_windows` | `AVG` and the statistics over a partition (one of a single line) and over `ROWS` frames empty at the first and last lines, two over columns never null (empty-frame their only reason) |
| `ledger.lenient` | run only under the caller's `ANSI_WARNINGS` and `ARITHABORT` off: capped decimal `+`, `*`, `/` and `-1.0 * decimal(38,0)` overflowing at the extremes; division and `%` by zero units; `decimal(38,0) % 1.0` (its operands brought to scale 1); `decimal(38,38) * bit` (the bit, 1, converted to `decimal(38,38)`); `ROUND` carrying (`decimal`, `money` to 3 places, `int`); `FLOOR`, `CEILING` and `ABS` at money's and int's ends; `POWER` overflowing, and to a fractional power of a negative (`domain-error`); `SQRT`, `LOG`, `LOG10` of zero and negative values; `EXP` overflowing; `AVG` over `decimal(38,0)` and over an overflowing product |
