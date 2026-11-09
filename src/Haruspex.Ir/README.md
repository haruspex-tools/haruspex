# The haruspex IR, version 0.2

The intermediate representation is the product: front ends write it, emitters read it, and it is a file, so
each side can be written in whatever language suits it. This page describes version 0.2. A JSON Schema for
it will follow.

## Why not JSON Schema

JSON Schema is an open validation language (`oneOf`, `allOf`, `if`/`then`, `$ref` anywhere), so every
emitter would have to handle all of it, and it has no notion of procedures, result sets, provenance or
unknowns. The IR is small and closed instead: an emitter that handles the records in `IrModel.cs` handles
every document. It maps to JSON Schema without loss.

## Shape of a document

```jsonc
{
  "irVersion": "0.2",                 // versioned separately from the tool
  "source": { "dialect": "tsql" },
  "objects": [{                       // sorted by id
    "id": "sales.get_order",          // the name in the database
    "name": "GetOrder",               // the name for generated code, settled once for every emitter
    "kind": "procedure",
    "at": "procedures/get_order.sql:1:1",
    "parameters": [{ "name": "@order_id", "type": { … }, "hasDefault": false, "output": false }],
    "results": [{                     // null when they cannot be listed
      "encoding": "json", "type": { "ref": "GetOrderRow" },
      "mayBeAbsent": true, "absentBecause": [{ "rule": "no-rows", "at": "procedures/get_order.sql:7:5" }]
    }]
  }],
  "types": {                          // named object types, sorted by name
    "GetOrderRow": { "kind": "object", "fields": [
      { "name": "shippedAt", "type": { "kind": "datetime", "format": "offset", "sql": "datetimeoffset(0)" },
        "presence": "omittedWhenNull", "nullable": true,
        "nullableBecause": [{ "rule": "nullable-column", "at": "sales.order.shipped_at" }] },
      { "name": "status", "type": { "kind": "string", "sql": "varchar(10)",
          "maxLength": { "value": 10, "unit": "bytes", "encoding": "single-byte", "from": { "type": "varchar(10)" } },
          "values": { "match": "collation", "items": ["open", "closed"], "from": [{ "check": "sales.ck_order_status" }] } },
        "presence": "always", "nullable": false },
      { "name": "total", "type": { "kind": "number", "format": "decimal", "sql": "decimal(19,2)",
          "digits": { "precision": 19, "scale": 2, "from": { "type": "decimal(19,2)" } },
          "min": { "value": 0, "inclusive": false, "from": { "check": "sales.ck_order_total" } },
          "max": { "value": 99999999999999999.99, "inclusive": true, "from": { "type": "decimal(19,2)" } } },
        "presence": "always", "nullable": false }
    ] }
  },
  "unknowns": [                       // everything that could not be typed, and why
    { "object": "sales.order_report", "path": "results[0][].customer_name",
      "reason": "unsupported-expression", "detail": "The expression upper(c.name) is not modelled yet.",
      "at": "procedures/order_report.sql:6:8" }
  ]
}
```

Type nodes are one of: a scalar (`{ "kind", "format"?, "sql", facets… }`, below), `{ "kind": "array", "items" }`,
`{ "ref" }`, `{ "kind": "json-text" }` or `{ "kind": "unknown", "reason" }`.

## Facets: what a scalar's values are guaranteed to be

A scalar may carry facets, each a guarantee about its **non-null** values, and each with `from`: where it came
from, `{ "type": "<type or alias type>" }` or `{ "check": "<schema.constraint>" }` (the constraint's location
when it has no name), for the origins graph. A facet that is not proved is left out: absence claims nothing.
None says anything about null, which is the field's `nullable`: a CHECK constraint passes when its condition is
UNKNOWN, as it is for null, so a constraint never makes a value non-null and a value set never excludes null.

| Facet | Meaning |
|---|---|
| `maxLength` / `length` | at most, or exactly (padded), `value` of `unit`: `utf16` (UTF-16 code units, as JavaScript counts) or `bytes` (of the stored encoding). With `bytes`, `encoding` is `single-byte` (a byte is a code unit: exact), `utf-8` or `double-byte` (a character takes several, so a maximum is an upper bound on UTF-16 length and a fixed length is only a maximum), or absent when unknown (no collation declared; a folder of scripts doesn't record the database's default) |
| `minLength` | at least `value` UTF-16 code units |
| `digits` | an exact decimal of at most `precision` digits, `scale` of them after the point |
| `min` / `max` | a bound, `inclusive` or not. `value` is a JSON number written from exact decimal text (`99999999999999999.99`, `-9223372036854775808`), so a reader that parses decimals loses nothing |
| `values` | the value is one of `items` (strings, or numbers as exact JSON numbers). `match` is `exact`, or `collation`: equal under the database's string comparison, which may ignore case, accents and trailing spaces, so `"Open "` can be a value of a set holding `"open"`. A `collation` set is true but not exact, and an emitter must not treat it as an enum |

Facets travel with a value only while it passes through unchanged: a column reference, under aliases, through
joins and FOR JSON nesting. Through any expression they are dropped, which leaves the type wider and still true.
Parameters carry no facets: a parameter's type converts what the caller sends (a string too long is cut short),
so its facets describe the value after conversion, not what a caller may pass.

The SQL Server front end derives them from types (`tinyint`'s range, `decimal(p, s)`'s digits and range,
`money`'s, string lengths and, from the collation, their encoding), alias types (`CREATE TYPE … FROM`, and
`sysname`), and CHECK constraints on one column: `IN`, `=` and OR-ed equalities give a set; `BETWEEN` and
comparisons combined by AND give bounds; `len(col) > n` and `col <> ''` give a minimum length. Anything else is
ignored, which is always safe. `Haruspex.SqlServer.Semantics` (`CheckReader`) says why each form is sound.

## Decisions

- **Presence is separate from nullability.** Each construct decides presence on its own: `FOR JSON` leaves
  a null key out unless `INCLUDE_NULL_VALUES` keeps it, while a rowset always has every column. An emitter
  needs both to tell an absent key (`.optional()` in Zod) from a null value (`.nullable()`).
- **Nullable is derived from its reasons.** A field is nullable exactly when `nullableBecause` is not empty,
  so the flag and its explanation cannot disagree, and non-null is only ever claimed with nothing against it.
- **Logical types, with the SQL type kept.** `string`, `number`, `boolean`, `date`, `time`, `datetime`,
  `uuid`. `format` refines them in neutral terms (`int32`, `int64`, `decimal`; `local` or `offset` for a
  `datetime`), because an emitter needs to know that a 64-bit integer does not fit a JavaScript number, not
  that it was a `bigint`. `sql` is kept for people and for emitters that want it.
- **Dates, times and identifiers are kinds, not strings.** Inside JSON they arrive as strings in a known
  format, and in a rowset as the driver's native values, so an emitter can only validate them if it knows
  which kind each one is: `date` (no time, no zone), `time` (no date, no zone), `datetime` with `local` (no
  offset: `datetime`, `datetime2`, `smalldatetime`) or `offset` (`datetimeoffset`), and `uuid` (8-4-4-4-12
  hex, not necessarily an RFC 9562 UUID). JSON *text* in a column (`json-text`) is unaffected: it is a
  string to parse.
- **A result can be absent.** A top-level `FOR JSON` query that matches no rows returns a result set with no
  row in it, not `[]`, `{}` or a NULL (observed, with and without `WITHOUT_ARRAY_WRAPPER` and
  `INCLUDE_NULL_VALUES`), so a JSON result has `mayBeAbsent: true` with the reason `no-rows`. A rowset is always
  present (with no rows, it is an empty result set). **Reader convention:** a reader that finds no row for
  a result that may be absent yields `[]` when the result's type is an array, and `null` otherwise. Emitters
  rely on it: a single-object result is nullable, an array result is not.
- **`WITHOUT_ARRAY_WRAPPER` is claimed as one object** without proving the query returns at most one row
  (WHERE is not read yet, so single-row queries are not recognised). The claim is accepted because it cannot
  be silently wrong: several rows produce `{...},{...}`, which is not valid JSON, so parsing fails loudly
  before any schema is applied.
- **Changes after a definition withdraw claims.** A table altered (other than by adding or checking
  constraints) or dropped after its `CREATE TABLE` becomes unknown (`unsupported-source`), and a procedure
  altered or dropped has unknown results (`redefined`), because which definition applies depends on the
  order scripts run. A CLR procedure's results are unknown (`external-code`).
- **Live JSON versus JSON text.** JSON built by `FOR JSON` nests as JSON. Once it is returned as a column
  it is a string holding JSON (`json-text`), which the consumer must parse.
- **A nested object that may be absent.** `FOR JSON PATH` builds nested objects from dotted aliases. Without
  `INCLUDE_NULL_VALUES` an object whose values are all null is left out, so it is `omittedWhenNull` with the
  reason `all-fields-null` when every field in it can be null. With `INCLUDE_NULL_VALUES` it is always
  written, its keys null, however deeply nested: `{"a":{"b":{"y":null}}}`. Both observed (see "Scalars in JSON").
- **Types live in a named table.** Names are decided once, by the front end, so every emitter agrees on them.
  Identical shapes are not merged yet.
- **Unknowns are listed at the top level** with the object, a path, a reason code and the source location,
  so a CI check can count and report them. The type node in place carries the reason code too.
- **Deterministic.** Keys in a fixed order, collections sorted, LF line endings, no byte order mark, no
  timestamps and no absolute paths: the same input gives the same bytes on every machine.

Changes within 0.1, before any release: with `INCLUDE_NULL_VALUES`, a nested object is now always present and
never null, as observed, where it was claimed only as possibly absent; the `time` kind was added, and SQL `time` is typed with it
instead of being unknown; results gained `mayBeAbsent` and `absentBecause`; the reasons `no-rows`,
`external-code` and `redefined` were added; a result's root object is named `<Procedure>Row` in every case.

## Scalars in JSON

What each kind claims about a JSON value, which the differential tests' validator checks:

| Kind | JSON value |
|---|---|
| `string` | a string, within its length facets (counted in their unit) and `values` |
| `number` | a number; with `uint8`, `int16`, `int32` or `int64`, an integer in that range (read exactly, not as a double); within its `min`, `max`, `digits` and `values` (compared exactly) |
| `boolean` | `true` or `false` |
| `date` | `"YYYY-MM-DD"` |
| `time` | `"hh:mm:ss"`, optionally with a fraction of a second |
| `datetime`, `local` | `"YYYY-MM-DDThh:mm:ss"`, optionally with a fraction, never an offset |
| `datetime`, `offset` | the same followed by `Z` or `+hh:mm`/`-hh:mm` |
| `uuid` | `"xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"` in hex digits |

The IR does not claim a precision. SQL Server's exact output was observed on SQL Server 2025 (RTM-CU10) and
is kept as tests in `tests/Haruspex.DifferentialTests/ForJsonObservationTests.cs`:

- A fraction of a second has exactly the type's scale in digits (`time(3)` gives `.100`, `datetime2(7)`
  `.1000000`, `datetime` three), and is **left out entirely when it is zero**: `datetime2(7)` can give
  `"2026-03-04T05:06:07"`. So a fraction may be absent whatever the scale; scale 0 never has one.
- `smalldatetime` always has `:00` seconds and no fraction.
- `datetimeoffset` writes a zero offset as **`Z`**, any other as `+hh:mm` or `-hh:mm`.
- `uniqueidentifier` is written in upper case.
- `decimal`, `numeric`, `money` and `smallmoney` keep their scale (`12.50`, `12.5000`); `bigint` is written in
  full, beyond 2^53; `float` and `real` in exponent form (`1.000000000000000e+000`). All are JSON numbers.
- `char(n)` and `nchar(n)` arrive padded with spaces to `n`: UTF-16 code units for `nchar`, bytes for `char`, so
  in a UTF-8 collation `char(3)` holding `é` arrives as `"é "` (kept as a test in `CheckConstraintObservationTests`).

Changes in 0.2: scalars gained facets (`maxLength`, `length`, `minLength`, `digits`, `min`, `max`, `values`),
each with its `from`. A reader of 0.1 that ignores unknown keys reads 0.2 documents unchanged, but the version
changed because the facets are claims a 0.2 reader may rely on.

Not in 0.2, but planned: origins (column lineage) beyond each facet's `from`, documentation, and a source hash.
