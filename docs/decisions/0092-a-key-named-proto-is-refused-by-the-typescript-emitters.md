# A key named `__proto__` is refused by the TypeScript emitters

**Date:** 21 Nov 2026

**Decision.** The Zod emitter refuses a document in which a field of a type it declares, a parameter or a table type's column
is named `__proto__` ([HAR5008](../diagnostics/HAR5008.md)), as it already refuses a result set of that name
([HAR5007](../diagnostics/HAR5007.md)), and the database module's emitter refuses a parameter of that name
([HAR5205](../diagnostics/HAR5205.md)). The Zod emitter also refuses a column of rows named as any other member every object
inherits, a name of `Object.prototype` such as `constructor`, `toString` or `valueOf` ([HAR5009](../diagnostics/HAR5009.md)),
which the mssql driver hands over as an array. The IR keeps each name: it is a claim about what SQL Server writes, and the JSON
Schema emitter writes it, since in JSON Schema a key of `properties` is only data. As keys of JSON, and as parameters, those
other names are ordinary, and so is `prototype` everywhere. This is provisional: recommended on 21 Nov 2026 and awaiting the
maintainer.

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
  Without a default the call fails; with one, the procedure runs with the default whatever the caller gave.
  Building a row, it reads each column's name on the row first, to find a column repeated, so a column named as any member of
  `Object.prototype` comes back as `[inherited member, value]`
  (`A_column_named_as_a_member_every_object_inherits_comes_back_as_an_array`). A parameter of such a name is sent, and a JSON
  key of such a name is an own key (`A_json_key_named_as_a_member_every_object_inherits_is_a_key_like_any_other`).
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
- *Type a column the driver hands over as an array as that array*, the inherited member and then the value. It describes the
  driver's handling of a repeated column, which may change in any version, and an application would read `row.constructor[1]`.
- *Refuse it in inference*, as an unknown. The IR's claim is true; it is the target language that can't hold it, which is the
  emitter's to say (as HAR5007 does for a result set).

**Reason.** No form of the key gives a schema that checks what the IR claims, and no schema of a column's value checks the
array the driver hands over for the other names, so writing either would be confidently wrong. A refusal names the key and
the fix, an alias, which costs one word of SQL. None of these names is one a real column is likely to have.

**In the code:** `TypeScriptNames.IsUnusableKey`, `TypeScriptNames.IsInheritedMember`, `ZodPlanner.RequireUsableKeys`,
`ZodPlanner.RequireDriverColumns`, and the parameter check in `FunctionPlanner.Plan`.
