# Fixture: parcels

An invented courier's parcels, for columns declared in `haruspex.json` to hold JSON of a schema
([decision 0093](../../../docs/decisions/0093-a-column-s-json-is-part-of-its-type-completed-by-the-config.md)): read plainly,
through `json_query`, nested by `FOR JSON`, and through a temporary table, a view, a variable, a scalar function and
`INSERT … EXEC` and `OUTPUT … INTO`. No part of it comes from a real system.

| Column | Declared as |
|---|---|
| `parcels.parcel.options` | [`options.schema.json`](schemas/options.schema.json): an object with two required keys and three that may be left out, one of them nullable, an `enum`, `minLength` and `maxLength` |
| `parcels.parcel.route` | [`stops.schema.json`](schemas/stops.schema.json), its definition `route`: an array of at least one stop, each of two kinds (`oneOf`, read as a union), each kind a `const` |
| `parcels.parcel.extra` | [`extra.schema.json`](schemas/extra.schema.json), which says nothing: any JSON ([decision 0094](../../../docs/decisions/0094-any-json-is-a-type-of-its-own.md)) |

| Procedure | Covers |
|---|---|
| `parcels.get_parcel` | a plain read: each column a string holding JSON of its schema |
| `parcels.parcel_feed` | `json_query` of each in `FOR JSON`: live JSON of its schema; `extra` narrowed to an object or an array |
| `parcels.options_text` | `FOR JSON` without `json_query`: a string holding JSON |
| `parcels.staged_options` | through a temporary table |
| `parcels.options_by_view` | through a view |
| `parcels.options_of_one` | through a variable, and a scalar function returning the column |
| `parcels.claimed_options` | through `UPDATE … OUTPUT … INTO` a table variable |
| `parcels.routes`, `parcels.staged_routes` | through `INSERT … EXEC` |

The seed has options with every key, with only the required ones, with a `null` note, and none at all; routes of one stop and of
both kinds; and `extra` as an object, an array and NULL. A run sets one `extra` to a JSON string, which any JSON allows and a plain
read returns as it is; `json_query` would refuse it with an error (13609), so no run that reads `extra` through `json_query`
sees it. Another removes every parcel, so that each `FOR JSON` result is seen absent.
