# A key named `__proto__` is refused by the TypeScript emitters

**Date:** 21 Nov 2026

**Decision.** The Zod emitter refuses a document in which a field of a type it declares, a parameter or a table type's column
is named `__proto__` ([HAR5008](../diagnostics/HAR5008.md)), as it already refuses a result set of that name
([HAR5007](../diagnostics/HAR5007.md)), and the database module's emitter refuses a parameter of that name
([HAR5205](../diagnostics/HAR5205.md)). The IR keeps the key: it is a claim about what SQL Server writes, and the JSON Schema
emitter writes it, since in JSON Schema a key of `properties` is only data. Every other name, `constructor` and `prototype`
included, is an ordinary key. This is provisional: recommended on 21 Nov 2026 and awaiting the maintainer.

**Context.** The Zod emitter printed such a key bare, `z.object({ __proto__: z.string() })`, which in JavaScript sets the shape
object's prototype rather than adding a key, so the schema had no such key. Observed with the versions `tests/emit-check` pins
(Zod 4.6.7, TypeScript 7.0.2, mssql 12.7.14, Ajv 8.20.0):

- SQL Server writes the key, as a `FOR JSON` alias and as a `json_object` key, and `JSON.parse` makes it an own property
  (`A_json_key_named_proto_is_a_key_of_the_document_parsed`).
- Bare or quoted (`"__proto__": …`), the key sets the prototype: the shape has no key, so `{"__proto__":5}` passes `z.object`
  and fails `z.strictObject` as an unknown key. Computed (`["__proto__"]: …`), it is an own key of the shape, but Zod skips a
  declared key of this name in every object, with and without its compiled parsers: `{"__proto__":5}` and `{}` both pass, and
  the key is left out of what it returns.
- In a TypeScript type, `{ __proto__: string }` is an ordinary required property, so a type Zod infers would claim a key the
  parsed value lacks.
- The mssql driver hands over no key for a column named `__proto__` (`A_column_named_proto_is_not_a_key_of_the_row_the_driver_hands_over`)
  and never sends a parameter named `@__proto__` (`A_parameter_named_proto_is_never_sent`): it keeps both as keys of an object.
- Ajv mishandles the key too: with `additionalProperties: false` it rejects `{"__proto__":"x"}`, and without it it accepts
  `{"__proto__":5}`. That is the validator's fault; the JSON Schema says what the value is.

**Alternatives.**
- *Write the key computed, `["__proto__"]`.* The shape then has the key, but Zod checks nothing there and returns a value
  without it, while the inferred type says it is present: a claim nothing holds.
- *Leave the key out of the schema.* `z.object` strips it, and the type no longer claims it, but `z.strictObject`
  (`"objects": "strict"`) then rejects every real value as having an unknown key, and the application loses the value without a
  word.
- *Type the key as `z.unknown()`.* It is still never checked and still dropped, and the argument for it rests on Zod's skipping
  staying as it is in every version.
- *Refuse it in inference*, as an unknown. The IR's claim is true; it is the target language that can't hold it, which is the
  emitter's to say (as HAR5007 does for a result set).

**Reason.** No form of the key gives a schema that checks what the IR claims, so writing one would be confidently wrong. A
refusal names the key and the fix, an alias, which costs one word of SQL. The name is not one a real column is likely to have.

**In the code:** `TypeScriptNames.IsUnusableKey`, `ZodPlanner.RequireUsableKeys`, and the parameter check in
`FunctionPlanner.Plan`.
