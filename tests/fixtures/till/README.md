# Fixture: till

An invented shop till, for procedures' parameters: one procedure for each group of types a generated client must check
before it sends a value, so that what SQL Server keeps of each is held to the IR's facts (`ParameterTests`,
`ParameterObservationTests`) and the generated parameter schemas check exactly that (`ParameterAcceptanceTests`). No part of
it comes from a real system.

| Procedure | Covers |
|---|---|
| `till.find_products` | `nvarchar(n)`, `char(n)` and `varchar(n)`; defaults |
| `till.price_band` | `decimal(p,s)`, `money` and `smallmoney` |
| `till.shift_times` | `date`, `datetime2(n)`, `datetime`, `smalldatetime`, `time(n)` and `datetimeoffset(n)` |
| `till.ring_up` | `int`, `smallint`, `bigint`, `tinyint`, `bit`, `uniqueidentifier`, `real` and `float`; an `OUTPUT` parameter and a return code |
| `till.add_lines` | a table-valued parameter and a `json` one, which are unknown to the client for now |

Its runs don't reach every edge: the edges of each type are sent one by one by the tests above.
