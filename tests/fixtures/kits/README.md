# Fixture: kits

An invented craft-kit catalogue for JSON built in a procedure and traced to where it is read (slice 6l). No part of it comes
from a real system. A kit has parts; each procedure builds JSON from them with `FOR JSON` or the constructors and reads it
back by another route: a variable, a table variable, a temporary table, a view, an inline function, a multi-statement
function, a CTE, a derived table, `SELECT … INTO`, a JSON literal, `OPENJSON` and `JSON_VALUE`. A stored JSON column
(`kit.kit.extras`) is read too, and stays unknown: someone else may write it.

The seed (`tests/Haruspex.DifferentialTests/Seeds/kits.sql`) reaches the edges the tracing turns on: a kit with three parts
(one without a colour, a full-length sku and colour, a quantity of 255), a free kit with no parts (every FOR JSON subquery over
its parts is NULL, every literal fallback is taken, and the multi-statement function returns no row for it), a kit with one
part and stored extras, a kit with no parts and no fallback, and kit ids that are not there, which leave a variable assigned
by a SELECT as it was and a FOR JSON over no rows absent.

| Procedure | Covers |
|---|---|
| `kit.get_kit` | `DECLARE @x nvarchar(max) = (SELECT … FOR JSON PATH)`; `SELECT @x = json_object(…, json_query(@y)) FROM … WHERE`, which may not assign; `json_query(COALESCE(@x, '[]'))`; `json_query(CASE … THEN '{…}' ELSE @x END)`, a union |
| `kit.kit_sheets` | a table variable filled by `INSERT … SELECT` and `UPDATE … SET '[]'`, copied into a temporary table and joined with a literal row; read through `json_query`, and as JSON text in a rowset |
| `kit.kit_routes` | a view's `FOR JSON` and `json_object` columns, an inline function's, a multi-statement function's return table (`OUTER APPLY`, no row for a free kit), a CTE, a derived table and `SELECT … INTO` of built JSON, each through `json_query` |
| `kit.part_rows` | `OPENJSON … WITH` over built JSON: each type back into itself, an integer into a wider one, a string into a shorter one, a key FOR JSON leaves out when null; `OPENJSON` without `WITH` over an object; `JSON_VALUE` and `OPENJSON … AS JSON` over a literal document |
| `kit.kit_badges` | a variable assigned on two paths (built JSON, or a literal), in a loop, a JSON literal of a tuple of two objects, a stored column (unknown), and a variable holding built JSON or other text, returned as a plain string |
