# Any JSON is a type of its own

**Date:** 21 Nov 2026

**Decision.** A column declared with the JSON Schema `{}` or `true`, which every JSON value satisfies, holds JSON of a type the IR
names: any JSON (`any-json`, IR 0.28), an object, an array, a string, a number, a boolean or `null`, of any content. It is not an
unknown: the column is known to hold JSON, so it nests as JSON in `FOR JSON` and `json_query` rather than as escaped text, Zod
checks it with `z.json()` and JSON Schema with `{}`, and it counts as declared, not as something haruspex can't see. Where SQL
Server's `json_query` returns it, it is narrowed to an object or an array (`objectOrArray`), since `json_query` returns nothing
else. The corpus figures count it apart from a typed shape.

**Context.** Some columns hold whatever their writer wants: a document whose shape is the business of the code that reads it.
A team can say so, and that is a real claim: the text is JSON, and nothing more is promised. An unknown says the opposite, that
haruspex couldn't tell, and reads as a gap to close.

**Alternatives.**
- *Read `{}` as an unknown.* It types the same in Zod (almost: `z.unknown()` accepts what isn't JSON) but says the wrong thing: the
  figures would count a column a team declared as one haruspex failed on, and the reason, `json-shape-unknown`, would be false.
- *Refuse `{}`, as the reader of declared results did.* A team with such a column could then declare nothing, and the column would
  stay unknown, or declare a narrower schema it can't vouch for.
- *Give any JSON no null, and make null the holder's nullability.* Neat inside an object, but JSON text whose content is `null` is
  a string the IR has no way to call nullable, so the type would refuse a value the declaration allows.

**Reason.** A type that says exactly what the team claims (JSON, of any shape) lets every consumer check that much, and tells a
reader of the figures the truth about which columns are open by design.

**In the code:** `IrAnyJson`; the Zod and JSON Schema emitters' `TypeLowering`; `DeclaredSchema` in the config layer; the IR README,
"Type nodes".
