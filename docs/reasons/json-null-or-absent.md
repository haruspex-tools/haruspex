# The null rule `json-null-or-absent`

A null rule, `IrNullRule.JsonNullOrAbsent` in the IR's model. A value may be null, or a result missing, because it is read from JSON, which may hold null or nothing there.

## What it means

It is read from JSON at a path, and the JSON holds `null` there or, read leniently, nothing or the wrong kind of value. In SQL Server: a column `OPENJSON` reads, or a `JSON_VALUE`, NULL where the JSON holds `null`, under a `strict` path too, and, under a lax path, where nothing is there, or an `AS JSON` column finds a scalar, or a scalar column or `JSON_VALUE` an object or array (or, for `JSON_VALUE`, a string longer than 4,000 characters). (A number too large for the column is NULL as well under the caller's `ANSI_WARNINGS` and `ARITHABORT` off.)


## What it rests on

Observed against SQL Server in `Json_value_is_nvarchar_4000_and_null_for_a_json_null_a_missing_path_an_object_or_a_long_string` (`FunctionObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
