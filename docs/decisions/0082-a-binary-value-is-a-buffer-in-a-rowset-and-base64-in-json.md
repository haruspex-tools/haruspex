# A binary value is a `Buffer` in a rowset and base64 in JSON

**Date:** 19 Nov 2026

**Decision.** Each target writes a binary value (decision 0081's `bytes`) in the form it arrives in:

| Where | Form | Zod | JSON Schema |
|---|---|---|---|
| a rowset, and an `OUTPUT` parameter | the Node `Buffer` the mssql driver hands over for `binary`, `varbinary`, `rowversion` and `image` alike | `z.instanceof(Buffer)`, its length checked with `.check(z.length(n))` when fixed and `.check(z.maxLength(n))` otherwise | the base64 text `FOR JSON` would write, as every rowset value is described (decision 0051) |
| `FOR JSON` and the JSON constructors | base64 text, the standard alphabet, padded with `=`, on one line, `""` for no bytes, `null` for NULL | `z.base64()`, of exactly or at most four characters for every three bytes or part of three (`.length(44)` for `binary(32)`, `.max(10668)` for `varbinary(8000)`) | `"type": "string"`, `"contentEncoding": "base64"`, the pattern of padded base64, and `minLength` and `maxLength` in characters |
| a parameter, and a table-valued parameter's column | a `Buffer`, sent with the driver's `VarBinary(n)`, which the server pads to a fixed length, and `VarBinary(MAX)` without a length | `z.instanceof(Buffer).check(z.maxLength(n))`: a fixed length is a maximum too, since a shorter value is padded | as a result's, a maximum only |

A parameter longer than its type is refused before anything is sent, as a string is (decision 0054): sent as `VarBinary(n)` the
server would fail the call with error 8016 (observed). The driver's `sql.Binary(n)` would pad and cut a value itself, without an
error, but its type declarations (`@types/mssql` 12.3.0) give `sql.Binary` no length, so the functions couldn't be compiled with it.

**Context.** The driver hands over every binary type as a `Buffer`, and refuses a plain `Uint8Array` as a parameter ("Invalid
buffer"), though a `Buffer` is one. `FOR JSON` writes a binary value as base64 text of the standard alphabet with padding and no
line breaks, past 8,000 bytes as before it, and the JSON constructors write it the same way (observed). So a rowset and a JSON
result of the same column are different values in JavaScript, as a date's are.

**Alternatives.**
- *`z.instanceof(Uint8Array)`*, which a browser has and Node's types needn't be loaded for. It would accept a `Uint8Array` the
  driver then refuses as a parameter, and the driver never hands one over. A module that holds a binary value in a rowset or a
  parameter already runs beside the driver, in Node, whose types name `Buffer`. A module of JSON results alone names neither.
- *Decode base64 in the schema* (`z.base64().transform(…)`), so both encodings give a `Buffer`. The schemas check what arrives and
  don't change it, as for dates (decision 0055's reasoning for what is sent applies to what is read too): a team decodes where it
  needs the bytes.
- *Leave a rowset's binary value out of the JSON Schema*, as a value JSON can't carry. A rowset is described as `FOR JSON` would
  write it, every column included, and `FOR JSON` writes a binary column as base64 text, so that is what the schema says.
- *`.refine((b) => b.length <= n)`* for a `Buffer`'s length. Zod 4's length checks take any value with a `length`, so
  `.check(z.maxLength(n))` says the same as a check Zod knows, with Zod's own message.

**Reason.** Each emitter claims what the database and the driver guarantee, in the form the value has where it is checked, and
nothing more. A `Buffer`'s length and a base64 text's are the same fact in two forms, so both are checked.

**In the code:** `Facets.Bytes` and `ParameterLowering` (Zod), `Scalars` and `SentScalars` (JSON Schema), `DriverTypes` (the
functions); `ZodEmitterTests`, `ZodParametersTests`, `JsonSchemaEmitterTests`, `MssqlEmitterTests`; observed in
`The_driver_hands_over_every_binary_type_as_a_buffer_and_sends_one_of_its_declared_length` and
`For_json_writes_a_binary_value_as_padded_base64_on_one_line`, and every run of the fixture `tests/fixtures/gallery` is parsed with
the generated schemas (`ZodAcceptanceTests`, `JsonSchemaAcceptanceTests`) and called through the generated functions
(`GeneratedFunctionTests`).
