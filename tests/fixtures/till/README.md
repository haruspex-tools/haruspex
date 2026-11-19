# Fixture: till

An invented shop till, for procedures' parameters and user-defined table types: one procedure for each group of types a
generated client must check before it sends a value, and table-valued parameters of table types with each kind of column, key
and constraint, so that what SQL Server keeps of each is held to the IR's facts (`ParameterTests`,
`ParameterObservationTests`) and the generated parameter schemas check exactly that (`ParameterAcceptanceTests`). No part of
it comes from a real system.

| Procedure | Covers |
|---|---|
| `till.find_products` | `nvarchar(n)`, `char(n)` and `varchar(n)`; defaults |
| `till.price_band` | `decimal(p,s)`, `money` and `smallmoney` |
| `till.shift_times` | `date`, `datetime2(n)`, `datetime`, `smalldatetime`, `time(n)` and `datetimeoffset(n)` |
| `till.ring_up` | `int`, `smallint`, `bigint`, `tinyint`, `bit`, `uniqueidentifier`, `real` and `float`; an `OUTPUT` parameter and a return code |
| `till.add_lines` | a table-valued parameter (`till.line_list`: a key, a CHECK and a nullable column) joined to a table, and a `json` parameter, which is unknown to the client for now |
| `till.grade_of` | one row by a table-valued parameter's unique key, and the value set its CHECK closes (`till.tag_list`, with a default) |
| `till.echo_readings` | a table type with a column of each type a client sends otherwise than as a string or an `int`, read with `SELECT *` |
| `till.count_serials` | a table type with an identity and a computed column, which no row can be sent in |
| `till.echo_echoes` | a key that drops a duplicate row (`IGNORE_DUP_KEY`), and a type and columns whose names need quoting |
| `till.basket_total` | a table variable of a table type, filled and read in the body: its key, CHECK and nullability as the type's |

Its runs don't reach every edge: the edges of each type are sent one by one by the tests above. A table-valued parameter is one
`runs.json` can't give, so the procedures that take one are called by `ParameterObservationTests` and through the generated
functions (`MssqlFunctionTests`) instead.
