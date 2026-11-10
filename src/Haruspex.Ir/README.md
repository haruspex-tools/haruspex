# The haruspex IR, version 0.12

The intermediate representation is the product: front ends write it, emitters read it, and it is a file, so
each side can be written in whatever language suits it. This page describes version 0.12. A JSON Schema for
it will follow.

## Why not JSON Schema

JSON Schema is an open validation language (`oneOf`, `allOf`, `if`/`then`, `$ref` anywhere), so every
emitter would have to handle all of it, and it has no notion of procedures, result sets, provenance or
unknowns. The IR is small and closed instead: an emitter that handles the records in `IrModel.cs` handles
every document. It maps to JSON Schema without loss.

## Shape of a document

```jsonc
{
  "irVersion": "0.12",                // versioned separately from the tool
  "source": { "dialect": "tsql" },
  "objects": [{                       // sorted by id
    "id": "sales.get_order",          // the name in the database: the object's identity
    "name": "GetOrder",               // the name for generated code, from the config file's naming rules
    "kind": "procedure",
    "at": "procedures/get_order.sql:1:1",
    "parameters": [{ "name": "@order_id", "type": { … }, "hasDefault": false, "output": false }],
    "results": [{                     // null when they cannot be listed
      "encoding": "json", "type": { "ref": "GetOrderRow" },
      "mayBeAbsent": true, "absentBecause": [{ "rule": "no-rows", "at": "procedures/get_order.sql:7:5" }],
      "cardinality": "atMostOne"      // how many rows: "one", "atMostOne" or "many"
    }]
  }],
  "types": {                          // named object types, sorted by name
    "GetOrderRow": { "kind": "object",
      "identity": { "procedure": "sales.get_order", "path": "results[0]" },   // what it is, whatever it is named
      "fields": [
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
  ],
  "overrides": [                      // claims the config file made rather than inference
    { "object": "sales.get_order", "path": "results[0].customer.email", "setting": "not-null",
      "why": "checkout requires an email" }
  ]
}
```

Type nodes are one of: a scalar (`{ "kind", "format"?, "sql"?, facets… }`, below; `sql` is left out only in declared results), an array, a tuple, a record, a
union (below), `{ "ref" }`, `{ "kind": "json-text", "content"? }`, `{ "kind": "import", "name", "from" }` or
`{ "kind": "unknown", "reason" }`.

| Node | Written as | Means |
|---|---|---|
| array | `{ "kind": "array", "items", "itemsNullable"?, "itemsNullableBecause"?, "minItems"?, "maxItems"? }` | any number of items of one type. Items are never null unless `itemsNullable` is `true`, with its reasons; `minItems` and `maxItems`, when there, bound how many there are |
| tuple | `{ "kind": "tuple", "items": [{ "type", "nullable", "nullableBecause"? }, …] }` | exactly these positions, in order, each with its own type and nullability |
| record | `{ "kind": "record", "values", "valuesNullable", "valuesNullableBecause"? }` | an object whose keys are data: any string keys (possibly repeated in the text, which `JSON.parse` reads as the last), each to a value of `values` |
| union | `{ "kind": "union", "variants": […] }` | a value of one of the variants: at least two, all different |
| import | `{ "kind": "import", "name", "from" }` | a value the config file declares to be of the application's own schema: `name`, exported by the module `from` (as the generated module writes it). Emitters import and use it as it is, so the value is still checked, by that schema, which receives the value as it arrives: the string, where it replaces JSON text |
| JSON text | `{ "kind": "json-text", "content"? }` | a string holding JSON, which the consumer parses. With `content`, a non-null string parses to a value of that type (the JSON was built here: a column of live JSON in a rowset, a `FOR JSON` subquery `WITHOUT_ARRAY_WRAPPER`, a call of a function returning JSON). Without it, only "JSON" is claimed (a `json` column, a parameter) |

## Paths

Unknowns, type identities and the config file's overrides write a place the same way (`IrPath.cs`): keys as `.key`, or
`["a key"]` (its text a JSON string) when the key is not an identifier; a tuple's position as `[0]`, a record's values as
`{}`, an array's items as `[]` and what JSON text parses to as `<json>`; a union's variants share their path. Inside a
recursive type the path starts at the type, named by its function, `<bom.components>.part.label`, because the type is
shared: the unknown is listed under every object whose results reach it.

An identifier, here and in every emitter's output, is an ASCII letter, `_` or `$` followed by letters, digits, `_` or
`$` (`IrIdentifiers.cs`): one rule, where the path renderer and the Zod emitter used to disagree about `$`.

## Names and identity

The IR keeps SQL names as the canonical identity: each object's `id`, and each object type's `identity`, the object
(`procedure`, or `function` for a recursive function's types) and the path that own it. Generated names (`name`, and
the keys of `types`) come from the config file's naming rules and overrides, applied by Haruspex.Config after
inference; an emitter uses them as they are and never reads the config file. Two types can share an identity: the
variants of a union share a path.

## Recursive types

An object type may refer to itself, directly or through other types: a scalar function that calls itself builds a
tree of any depth (a bill of materials, a category's subcategories). Such a type is written once in `types` and marked
`"recursive": true`; its fields refer to it by `{ "ref" }` like any other.

**A reader detects a cycle** by the flag, or by itself: follow `ref`s (through arrays, tuples, records, unions and
JSON text's `content`) keeping the names it is expanding, and stop at a name already on that list. A reader that
inlines references must do one or the other, or it never ends. The flag is set exactly on the types that can reach
themselves.

The type does not say how deep a tree goes. SQL Server stops at 32 nested calls of a function with error 217
(observed), which fails the query rather than returning a value, so no output is deeper than that, but the IR does not
express the bound.

## Facets: what a scalar's values are guaranteed to be

A scalar may carry facets, each a guarantee about its **non-null** values, and each with `from`: where it came
from, `{ "type": "<type or alias type>" }` or `{ "check": "<schema.constraint>" }` (the constraint's location
when it has no name), or `{ "engine": "<what>" }` for a closed set SQL Server itself writes (0.9: `$action`), or `{ "expression":
"<location>" }` for the literals a `CASE`, `IIF` or `CHOOSE` gives (0.12), for the origins graph. A facet that is not proved is left out: absence claims nothing.
None says anything about null, which is the field's `nullable`: a CHECK constraint passes when its condition is
UNKNOWN, as it is for null, so a constraint never makes a value non-null and a value set never excludes null.

| Facet | Meaning |
|---|---|
| `maxLength` / `length` | at most, or exactly (padded), `value` of `unit`: `utf16` (UTF-16 code units, as JavaScript counts) or `bytes` (of the stored encoding). With `bytes`, `encoding` is `single-byte` (a byte is a code unit: exact), `utf-8` or `double-byte` (a character takes several, so a maximum is an upper bound on UTF-16 length and a fixed length is only a maximum), or absent when unknown (no collation declared; a folder of scripts doesn't record the database's default) |
| `minLength` | at least `value` UTF-16 code units |
| `digits` | an exact decimal of at most `precision` digits, `scale` of them after the point |
| `min` / `max` | a bound, `inclusive` or not. `value` is a JSON number written from exact decimal text (`99999999999999999.99`, `-9223372036854775808`), so a reader that parses decimals loses nothing |
| `values` | the value is one of `items` (strings, or numbers as exact JSON numbers). `match` is `exact`, or `collation`: equal under the database's string comparison, which may ignore case, accents and trailing spaces, so `"Open "` can be a value of a set holding `"open"`. A `collation` set is true but not exact, and an emitter must not treat it as an enum. `padded`: one of the items followed by any number of spaces (U+0020), as under a binary collation, which compares exactly but, like every comparison in SQL Server, ignores trailing spaces. String items of a `CHECK`'s set never end in a space: SQL Server pads the shorter side before comparing, so `'a'` passes `IN ('a ')`, and the front end removes an item's trailing spaces. Those of an `expression`'s set (0.12) are the values written, trailing spaces and all |

Facets travel with a value only while it passes through unchanged: a column reference, under aliases, through
joins, FOR JSON nesting and the JSON constructors. Through any expression they are dropped, which leaves the type wider and still true.
Parameters carry no facets: a parameter's type converts what the caller sends (a string too long is cut short),
so its facets describe the value after conversion, not what a caller may pass.

The SQL Server front end derives them from types (`tinyint`'s range, `decimal(p, s)`'s digits and range,
`money`'s, string lengths and, from the collation, their encoding), alias types (`CREATE TYPE … FROM`, and
`sysname`), and CHECK constraints on one column: `IN`, `=` and OR-ed equalities give a set; `BETWEEN` and
comparisons combined by AND give bounds; `len(col) > n` and `col <> ''` give a minimum length. Anything else is
ignored, which is always safe. `Haruspex.SqlServer.Semantics` (`CheckReader`) says why each form is sound.

Only a **trusted** constraint is read. One disabled by `ALTER TABLE … NOCHECK CONSTRAINT` checks no row, and one
added `WITH NOCHECK` never checked the rows already there, so neither guarantees anything; they are ignored, as is
`NOT FOR REPLICATION`. A NOCHECK counts wherever it is in a script, under IF or in a procedure body
(the bulk-load pattern) included, and when its table has no schema it may mean a table of that name in any schema;
when it names a constraint the table doesn't declare, it may mean an unnamed one by its generated name. A disabled
constraint counts again only when `WITH CHECK CHECK CONSTRAINT`, which validates every row, follows it in the same
script as a statement of its own, naming the table with its schema (across scripts the order isn't known, and inside
a body it may not run).

**The facts are about the project, not about a live database.** They hold for a database deployed from the project
and not changed since. A database can drift from its project, for example through a constraint disabled by hand on
the server, a column altered outside a deployment, rows written while a constraint was off, or a NOCHECK run from
dynamic SQL (`EXEC` of a string), whose text isn't visible. Static analysis can't see any of that. Comparing a deployed database with its project is a job for a schema-compare or a later haruspex check,
not for these facts.

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
- **A result can be missing.** A result returned inside a branch, or after a `RETURN` that may be taken, may not run,
  and then its result set is not there at all, not empty; a result after it arrives in its place (observed). Results
  are listed by position: `results[k]` is everything a successful path can return k-th, a `union` of the whole results when
  paths differ (a rowset is then a union of arrays: every row of one result set comes from one statement). A position some path does not reach
  has `not-run` in `absentBecause`, at each statement that can return it there, and so does every position after it:
  the result sets a client receives are always the first ones listed. **Reader convention:** a reader pads the
  missing ones as absent, `[]` for a rowset or an array result and `null` otherwise, as for a JSON result with no row.
- **The IR describes calls that succeed.** A path that ends in an error the caller sees (`THROW`, `RAISERROR` of
  severity 11 or more, observed to fail the call even where the procedure goes on) describes no call that returns,
  and is left out: `IF @x IS NULL THROW …` is a guard, after which the results always run.
- **A result says how many rows it returns** (`cardinality`, 0.8): `one`, `atMostOne` or `many`, counting a rowset's
  rows, or a `FOR JSON` result's objects (its array's items, or its one object without the array wrapper). It is how many
  the query returns when it runs; whether the result set may be missing is `absentBecause`. `one` is an aggregate query
  without `GROUP BY` or `HAVING`, or a query with no `FROM` and no filter; `atMostOne` adds `TOP (1)`, `FETCH NEXT 1`, and
  a filter that fixes every column of a primary key or unique constraint with `=` in the key's own type (or `IS NULL`: a
  key holds one NULL at most); `many` claims nothing. A generated client can return one row, or a row or null, rather
  than a list. Emitters bound an array result by it, except one that may be absent, which reads as `[]` and is only
  bounded above. A `FOR JSON` query that always returns exactly one row is never absent (observed).
- **`WITHOUT_ARRAY_WRAPPER` is claimed as one object** even where the query is not proved to return at most one row
  (`cardinality` is `many`). The claim is accepted because it cannot be silently wrong: several rows produce
  `{...},{...}`, which is not valid JSON, so parsing fails loudly before any schema is applied. Where `cardinality` is
  `one` or `atMostOne`, it is proved and the caveat does not apply.
- **Filters refine.** A column is non-null where a `WHERE`, an inner join's `ON` or a `HAVING` keeps only rows in which
  it cannot be null, by three-valued logic (`@x IS NULL OR c = @x` does not narrow `c`); an outer join's optional side
  is not null-extended where a filter proves its row matched; an aggregate query's scalar subquery is never empty;
  `COUNT_BIG` is never null, and `COUNT` only for `count-overflow`. The rules, each observed, are in
  `Haruspex.SqlServer.Inference/README.md` ("Refinement"). A `FOR JSON` subquery that is not null has at least one item,
  since over no rows it is NULL, not `[]`.
- **Changes after a definition withdraw claims.** A table altered (other than by adding or checking
  constraints) or dropped after its `CREATE TABLE` becomes unknown (`unsupported-source`), and a procedure
  altered or dropped has unknown results (`redefined`), because which definition applies depends on the
  order scripts run. A CLR procedure's results are unknown (`external-code`).
- **Live JSON versus JSON text.** A `FOR JSON` subquery with its array wrapper, a JSON constructor, `json_query`
  over any JSON, and a value of the native `json` type nest as JSON, in `FOR JSON` and in the constructors, also
  through a scalar subquery. Any other string is escaped as text, even one holding JSON: a `FOR JSON` subquery
  `WITHOUT_ARRAY_WRAPPER` nests as a string (`{"a":"{\"x\":1}"}`, so `json-text`) until `json_query` makes it JSON
  again, and JSON passed through a variable is text too (all observed). Once JSON is returned as a column it is a string holding JSON (`json-text`), which the
  consumer must parse; a `json` column and a `json` parameter are `json-text` too. A `json` value inside JSON is
  live but of unknown shape (`json-shape-unknown`): its shape is in the data.
- **The JSON constructors** (see "JSON constructors" below) give objects, tuples, arrays and records.
- **Scalar functions** (see "Scalar functions" below) carry the JSON their body returns, and recursive ones are named
  recursive types.
- **A nested object that may be absent.** `FOR JSON PATH` builds nested objects from dotted aliases. Without
  `INCLUDE_NULL_VALUES` an object whose values are all null is left out, so it is `omittedWhenNull` with the
  reason `all-fields-null` when every field in it can be null. With `INCLUDE_NULL_VALUES` it is always
  written, its keys null, however deeply nested: `{"a":{"b":{"y":null}}}`. Both observed (see "Scalars in JSON").
- **Types live in a named table.** Names are decided once, from the config file, before any emitter runs, so every
  emitter agrees on them. Identical shapes are not merged yet.
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

## JSON constructors

What each constructor gives, every rule observed on SQL Server 2025 RTM-CU10 and kept as a test in
`tests/Haruspex.DifferentialTests/JsonConstructorObservationTests.cs`:

| Constructor | Default null option | IR |
|---|---|---|
| `json_object('k': v, …)` | `NULL ON NULL`: a null value's key is kept, with null | an object; each field's presence is `always`, or `omittedWhenNull` under `ABSENT ON NULL`. Never null. Keys must be literals: a key that is an expression makes the keys data (`unsupported-expression`), and a repeated key is written twice (`duplicate-column`). Keys differing in case are two keys |
| `json_array(v, …)` | `ABSENT ON NULL`: a null element is dropped and the rest close up | under `NULL ON NULL`, or when no element can be null, a tuple; otherwise an array of the elements' types (a union when they differ) with `minItems` the elements never null and `maxItems` all of them. Never null |
| `json_arrayagg(v [ORDER BY …])` | `ABSENT ON NULL` | an array of v; items nullable only under `NULL ON NULL`; `minItems` 1 when no item can be dropped |
| `json_objectagg(k: v)` | `NULL ON NULL` | a record of v; values nullable only under `NULL ON NULL`. Every key is a string, whatever k's type; a repeated key is written each time |

- **The aggregates over no rows are `NULL`**, not `[]` or `{}`, so without `GROUP BY` they are nullable with the reason
  `empty-aggregate`. Under `GROUP BY` (with `ROLLUP` and `GROUP BY ()` too) no rows give no groups at all, so there
  they are never null. A correlated subquery around one without `GROUP BY` always has its one row, so it adds nothing
  (0.8; it added `empty-subquery` before); with `GROUP BY` it adds `empty-subquery`.
- **Scalars are written as `FOR JSON` writes them** ("Scalars in JSON" below holds for the constructors too): the
  same strings for `date`, `time(n)`, `datetime2(n)`, `datetimeoffset(n)`, `datetime`, `smalldatetime` and
  `uniqueidentifier`, `true`/`false` for `bit`, decimals and money with their scale, `bigint` in full, `char(n)`
  padded. A value keeps its facets while it passes through unchanged.
- `RETURNING json` changes the value's SQL type (`json` rather than `nvarchar(max)`), not what is written.
- **Availability:** `json_object` and `json_array` exist from SQL Server 2022, `json_arrayagg`, `json_objectagg` and
  the `json` type from SQL Server 2025 and Azure SQL. haruspex does not know the target's version yet, so it types
  them whatever the target; a procedure using one the target lacks fails when it is created.

## Scalar functions

A call of a scalar user function is typed from the function. Observed on SQL Server 2025 RTM-CU10 and kept as tests
in `tests/Haruspex.DifferentialTests/JsonFunctionObservationTests.cs`:

- **A function returning JSON** (`nvarchar(max)` or `json`) whose body is `RETURN (SELECT … FOR JSON …)`,
  `RETURN json_object(…)` (any constructor, or `json_query`), or a variable assigned one of them and returned, carries
  that JSON's shape. A plain call returning `nvarchar(max)` is text, escaped wherever it nests (`{"x":"[{\"id\":2}]"}`):
  `json-text` with the shape as `content`. `json_query(f(…))` embeds it as JSON, and so does a call of a function
  returning `json`, without `json_query`.
- **Its nullability is the body's.** FOR JSON over no rows returns NULL from the function (so a FOR JSON PATH key
  holding it is left out: `omittedWhenNull`, `empty-subquery`); a constructor never returns NULL.
- **Parameters** inside the body are nullable (`parameter`), since the shape is the same for every call, and keep their
  type's facets: an argument is converted to the parameter's type (a string cut short, a decimal rounded). With
  `RETURNS NULL ON NULL INPUT` they are non-null, because a null argument makes the call NULL without running the
  body; such a call is nullable (`null-input`) when an argument may be null.
- **Any other function** gives its declared return type, nullable (`function-result`): a scalar function may return
  NULL from any body. That includes a JSON body returned as a type shorter than `nvarchar(max)` (observed:
  `nvarchar(10)` cuts the text to `[{"id":1,"`), and any body with control flow.
- **A function that calls itself**, directly or through others, gives a recursive type named after it
  (`<Function>Node`): its object, or the object items of its array. It is found as a least fixed point, starting from
  no information and reading every body again until nothing changes, at most 4 steps per function in the cycle plus 4.
  A cycle that reaches no fixed point by then, or a function every return of which calls itself again, is unknown
  (`unresolved-recursion`). `Haruspex.SqlServer.Inference/FunctionSummaries.cs` says why the method is sound.
- An altered or dropped function is unknown (`redefined`), as a procedure is; a function not in the project, or a
  table-valued one called as a scalar, is unknown (`unsupported-expression`).

## Statement flow

A procedure's statements are read in order (slice 6), through temporary tables (`#t`), table variables (`@t`),
variables, `IF`, `WHILE`, `TRY … CATCH` and CTEs, recursive ones included. What each gives, and why each join is
sound, is in `Haruspex.SqlServer.Inference/README.md`; the engine rules are observed and kept as tests in
`tests/Haruspex.DifferentialTests/StatementFlowObservationTests.cs`. What it means for the IR:

- A value that passes unchanged through a temporary table or a CTE keeps its facets and its origins: `nullable-column`
  still names the base column, and a facet's `from` still names the base table's CHECK. A temporary table's or table
  variable's own CHECK is named by its table: `{ "check": "#totals.ck_amount" }`, and its nullable columns as
  `#totals.amount`.
- A variable has its declared type, with the type's facets, and is nullable (`variable`) until a value that is never
  null is assigned on every path. A procedure's parameter read in its body is nullable (`parameter`).
- A `#t` the procedure does not create on every path before it reads it is the caller's, if any: unknown,
  `caller-temp-table`. Results that cannot be lined up by position are unknown, `varying-results`.

## Calls and declared results

A procedure's `EXEC` of another of the project is followed (slice 6b): the callee's result sets are its caller's at that
point, as if its statements were there, so they carry the callee's locations (a `not-run` or `no-rows` reason, an unknown's
`at`). What the engine does across a call is in `Haruspex.SqlServer.Inference/README.md` ("Calls"). For the IR:

- Calls whose results the project cannot show give the unknown reasons `dynamic-sql`, `procedure-variable`,
  `system-procedure`, `missing-procedure` and `ambiguous-procedure` (and `external-code` for a CLR procedure, as before).
- **Declared results.** The config file can declare a procedure's results where they can't be inferred. A selected
  procedure's own declared results are its `results`, with an override `{ "object", "path": "results", "setting":
  "results", "why" }`; a caller whose results include a declared callee's lists the same override with `"via"`, the
  callee's name. So every result that rests on a person's word, not the database's, is listed.
- Inside declared results, a value that may be null, or a key that may be left out, or a result that may be absent, has the
  reason `declared`, at the procedure declared. A declared scalar has no SQL type, so it has no `sql`: that key is the
  database's type, which other dialects and backends read, and a JSON Schema type is not one.

## Writes, triggers and cursors (0.9)

Statements that write tables are read (slice 6d); the engine rules, each observed, are in
`Haruspex.SqlServer.Inference/README.md` ("DML", "Triggers", "Cursors"). For the IR:

- A write returns no result set, on any table, unless it has an `OUTPUT` without `INTO`, which is a rowset result of
  `cardinality` `many`, always there (with no rows when nothing was written).
- In a `MERGE`'s `OUTPUT`, `$action` is a string whose `values` are the actions the statement has, `match` `exact`, from
  `{ "engine": "$action" }`; a column with no value for some of its actions (`deleted` for rows inserted, `inserted` for
  rows deleted, the source's for rows not matched by the source) has the null rule `merge-action`, at the statement.
- A write that fires a trigger of the project that may return result sets makes the results unknown,
  `trigger-results`, its detail naming the trigger and the statement in it.
- `FETCH` without `INTO` makes the results unknown, `hidden-column`: its row carries `ROWSTAT`, a column the server marks
  hidden, which some drivers list and others leave out, so the row's keys depend on the client.

## APPLY and views (0.10)

`CROSS APPLY`, `OUTER APPLY`, `VALUES` lists and views of the project are read (slice 6e); the engine rules, each observed,
are in `Haruspex.SqlServer.Inference/README.md` ("APPLY", "Views"). For the IR:

- A column from the right side of an `OUTER APPLY` has the null rule `outer-apply`, at the right side, unless the applied
  query always returns a row (an aggregate without `GROUP BY`, a query without `FROM`, a `VALUES` list) or a filter proves
  the row matched. It is the `OUTER APPLY` counterpart of `outer-join`, kept apart because the fix differs: an applied query
  that always returns a row removes it.
- A view's columns carry their reasons from its definition: a `nullable-column` names the base table's column, an
  `outer-join` or `outer-apply` inside the view points into the view's file.

## Expressions, table-valued functions and read-uncommitted (0.11)

The common expressions, table-valued functions as table sources, and visible read-uncommitted are read (slice 6f); the engine
rules, each observed, are in `Haruspex.SqlServer.Inference/README.md` ("Expressions", "Table-valued functions",
"Cardinality"). For the IR:

- An expression's scalar has the SQL type SQL Server gives it (`decimal(38,2)` for `SUM` of a `decimal(10,2)`,
  `nvarchar(15)` for `CONCAT` of a `varchar(5)` and an `nvarchar(10)`), with that type's facets only: a column's `CHECK` set or
  bounds do not survive an expression, except a conversion to the column's own type or `ISNULL` of a value never null, which
  give the value unchanged. The differential tests compare each rowset column's `sql` with the server's own description.
- **`arithmetic-overflow`**: arithmetic, a `SUM`, or a conversion (`CAST`, `CONVERT`, an assignment, a function's argument)
  that can overflow its type, or a division that can divide by zero. By default that is an error, which fails the call; under
  a caller's `ANSI_WARNINGS` and `ARITHABORT` off it is NULL, and a procedure cannot prevent that, as for `count-overflow`. So
  `CONVERT(decimal(19,2), SUM(amount))` is nullable for it, and `COALESCE(SUM(amount), 0)` is not. A conversion that cannot
  overflow (to a wider type, a string long enough, a date into a string) does not have it.
- **`failed-conversion`**: `TRY_CAST` or `TRY_CONVERT` of a value that may not convert, which is NULL rather than an error.
- `COALESCE` and `ISNULL` are never null when an argument is never null, except that `COALESCE` reads an argument with a
  subquery or a call twice, so one before the never-null argument keeps its reasons. `CONCAT` and `CONCAT_WS` are never null.
  String `+` is null when an operand is, which holds whether or not the session concatenates NULL as empty.
- A table-valued function's columns carry their reasons from its body or its return table: a `nullable-column` names the base
  table's column, or `<function>.<column>` for a multi-statement function's declared table; an argument that may be null keeps
  its reasons through the body, and one not modelled is `parameter`. A result may be `atMostOne` through a function's key or
  its one row per call.
- A `NOLOCK` or `READUNCOMMITTED` hint, or `READ UNCOMMITTED` set in the procedure or a caller, removes the keys of what it
  reads, so a result once `atMostOne` by a key is `many`. An isolation level the outermost caller sets is not seen: the
  IR assumes it does not read uncommitted.

## CASE, IIF, CHOOSE, NULLIF and COALESCE over JSON (0.12)

`CASE`, `IIF`, `CHOOSE` and `NULLIF`, and `COALESCE` and `ISNULL` over JSON, are read (slice 6g); the engine rules, each
observed, are in `Haruspex.SqlServer.Inference/README.md` ("CASE, IIF, CHOOSE and NULLIF", "COALESCE and ISNULL over JSON").
For the IR:

- A `CASE`, `IIF` or `CHOOSE` has the SQL type SQL Server gives it, with that type's facets. When every result other than
  `NULL` is a literal, it has a `values` facet of those literals, `match` `exact`, `from` `{ "expression": "<location>" }`.
  These are values the procedure writes, so they are exact, not compared under a collation, and a string item keeps its
  trailing spaces: `CASE … THEN 'a ' …` gives `"a "` (a literal is `varchar`, never padded). The rule above that string items
  never end in a space is about the sets a `CHECK` gives.
- New null rules: **`no-else`** (a `CASE` without `ELSE`, NULL when no `WHEN` holds), **`null-literal`** (a result that is the
  `NULL` literal, at it), **`out-of-range`** (a `CHOOSE` whose index may be below 1 or above its count), **`nullif`** (a
  `NULLIF`, NULL when its arguments are equal; otherwise it is its first argument with its facets).
- **`read-twice`**: a `COALESCE` whose argument holding a subquery or a call, before one never null, is read a second time
  after it was tested, and may then be NULL. It replaces the reasons that argument had: an argument tested NULL is passed
  over, so only its second read can make the `COALESCE` NULL. In 0.11 the argument's own reasons were given, which was
  sound but named the wrong cause. No seed can reach it, as for `arithmetic-overflow`.
- A branch knows its condition: in `CASE WHEN x IS NULL THEN 0 ELSE x END` the `ELSE`'s `x` is never null, whatever made it
  nullable (a column, an outer join, a `ROLLUP`), so the `CASE` is not.
- `COALESCE` over JSON is `json-text` wherever it nests, its `content` what the arguments hold (a fallback `'[]'` removes the
  array's `minItems`, `'{}'` adds the empty object as a variant of a `union`); `ISNULL` over JSON is JSON where it nests, with
  the same content, and never null with a literal fallback. A `CASE`, `IIF`, `CHOOSE` or `NULLIF` over JSON is unknown
  (`unsupported-expression`): whether it nests as JSON depends on whether the server folds it.

Changes in 0.12 (slice 6g, CASE and COALESCE over JSON): the null rules `no-else`, `null-literal`, `out-of-range`, `nullif`
and `read-twice`; the fact source `expression`. These expressions were unknown (`unsupported-expression`) before, and a
`COALESCE` read twice gave its argument's reasons instead of `read-twice`. A 0.11 reader would meet null rules and a fact
source it does not know, which is why the version changed.

Changes in 0.11 (slice 6f, expressions and table-valued functions): the null rules `arithmetic-overflow` and
`failed-conversion`. Expressions and table-valued functions were unknown (`unsupported-expression`, `unsupported-source`)
before; a key read uncommitted no longer bounds a result. A 0.10 reader would meet a null rule it does not know, which is why
the version changed.

Changes in 0.10 (slice 6e, APPLY and views): the null rule `outer-apply`. APPLY, `VALUES` and views were unknown
(`unsupported-source`, `unresolved-table`) before; a result may now be `atMostOne` through an APPLY or a keyed view. A 0.9
reader would meet a null rule it does not know, which is why the version changed.

Changes in 0.9 (slice 6d, writes): the unknown reasons `trigger-results` and `hidden-column`; the null rule
`merge-action`; the fact source `engine`. Writes to permanent tables, `OUTPUT`, `WITH` before a write and cursors were
`unsupported-statement` before. A 0.8 reader that does not know `engine` would read a `values` facet's `from` it cannot
place, which is why the version changed.

Changes in 0.8 (slice 6c, refinement): a result's `cardinality`; the null rules `count-overflow` and `grouping-set` (a
grouped column of a query grouped by `ROLLUP`, `CUBE` or `GROUPING SETS`, NULL in its super-aggregate rows; such a query's
grouped columns were wrongly claimed non-null before). Refinement tightens
nullability and adds `minItems`/`maxItems` to `FOR JSON` subqueries' arrays, which 0.7 readers read as before, but a 0.7
reader would not know a result is one row, and a document always writes `cardinality`, which is why the version changed.

Changes in 0.7 (slice 6b, calls): the unknown reasons `dynamic-sql`, `procedure-variable`, `system-procedure`,
`missing-procedure` and `ambiguous-procedure`; the null rule `declared`; the override setting `results` and an override's
`via`; a scalar's `sql` may be left out (in declared results). A 0.6 reader that ignores unknown keys would read a caller's declared results as inferred, which is why the version
changed.

Changes in 0.6 (slice 6, statement flow): the result reason `not-run`, under which a result set may be missing
altogether (only trailing ones, padded by readers as absent); a rowset result that is a `union` of arrays of rows;
the null rule `variable`; the unknown reasons `caller-temp-table` and `varying-results`; and results described for
calls that succeed, so a path that throws no longer makes them unknown. A 0.5 reader would take a missing result set
for a wrong count, which is why the version changed.

Changes in 0.5 (slice 4, the config file): an object type's `identity`; the `import` node; the document's `overrides`;
names settled from the config file (the schema left out unless its block gives a `prefix`), with no number added to
break a clash between objects, which is an error instead; a path inside a recursive type starts with the function's name
(`<bom.components>`), not the type's; and `$` counts as an identifier character in paths (`.a$b`, not `["a$b"]`). A 0.4
reader that ignores unknown keys would miss the `import` node and the overrides, which is why the version changed.

Changes in 0.4: an object type's `recursive` flag, and references that form cycles; JSON text's `content`; the null
rules `function-result`, `parameter` and `null-input`; the unknown reason `unresolved-recursion`. A 0.3 reader that
expands references would loop on a recursive type, which is why the version changed.

Changes in 0.3: the array's `itemsNullable`, `itemsNullableBecause`, `minItems` and `maxItems`; the `tuple`, `record` and
`union` nodes; the null rule `empty-aggregate`. An array FOR JSON builds is written as in 0.2. A 0.2 reader that ignores
unknown keys would read an array's possibly-null items as never null, which is why the version changed.

Changes in 0.2: scalars gained facets (`maxLength`, `length`, `minLength`, `digits`, `min`, `max`, `values`),
each with its `from`. A reader of 0.1 that ignores unknown keys reads 0.2 documents unchanged, but the version
changed because the facets are claims a 0.2 reader may rely on.

Not in 0.12, but planned: origins (column lineage) beyond each facet's `from` and each null reason's `at`, documentation, and a source hash.
