# A decimal's places are a note in JSON Schema, not `multipleOf`

**Date:** 16 Nov 2026

**Decision.** The JSON Schema emitter writes a decimal of no places, `decimal(p, 0)`, as `integer`, and a decimal of some places
as `number` with its bounds and no `multipleOf`; its description says how many places it has ("At most 2 decimal places, which
multipleOf would check inexactly"), and the document's description says why once. The Zod emitter keeps `.multipleOf(1e-s)`,
because Zod's test of a step is shown never to reject a true value.

**Context.** `decimal(7,2)` holds only multiples of 0.01, and JSON Schema has the keyword for it: `multipleOf: 0.01`. Its
specification defines it on the mathematical values. Most validators read a JSON number as a binary double and divide: Ajv
8.20.0, in its default configuration, rejects 0.07 as a multiple of 0.01 (checked), since 0.07 / 0.01 is 7.000000000000001 in
binary floating point; in Python, `json` reads numbers as floats unless told to parse them as decimals, and `jsonschema` then divides floats too. A schema with
`multipleOf` would reject values SQL Server returns, which is worse than claiming less.

**Alternatives.**
- *`multipleOf` as the specification defines it*, with a note that a validator must compare exactly: Ajv's `multipleOfPrecision`
  option, Python's `json.loads(…, parse_float=Decimal)`. Correct on paper, but a team that validates with the defaults gets false
  rejections in production, from a schema haruspex wrote. The note would be read after the failure, not before.
- *A setting* (`"decimalSteps": "multipleOf"`) for teams whose validators compare exactly. A setting for one keyword of one
  emitter, whose wrong value fails at run time; it can be added if a team asks, and would sit beside this record.
- *A `pattern`.* JSON Schema's `pattern` applies to strings only, and a decimal is a JSON number.

**Reason.** Never claim more than a validator will check correctly: a widened type with a note is true everywhere, and the
places are still said. A decimal of no places loses nothing, since `integer` is exact in every validator.

**In the code:** `Numbers.Of`, and the caveat `JsonSchemaCaveat.DecimalSteps`.
