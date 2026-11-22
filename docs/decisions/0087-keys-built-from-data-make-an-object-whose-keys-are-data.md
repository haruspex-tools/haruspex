# Keys built from data make an object whose keys are data

**Date:** 20 Nov 2026

**Decision.** A JSON object assembled as text whose every key is built from data in quotes, each safe there (`'{' +
string_agg('"' + string_escape(k, 'json') + '":' + v, ',') + '}'`), is typed as an object whose keys are data, as
`json_objectagg` builds (`RecordShape`), its values the join of every value written. A key built from data beside keys written in
literals makes the object unknown. An object of no keys joins such an object as itself, in the lattice of JSON shapes
(`JsonShapes.Join`), since it is one with no keys: `'{' + coalesce(string_agg(…), '') + '}'` is `{}` over no rows.

**Context.** Typing objects assembled as text asked whether the IR can say an object of unknown keys whose values are all
known. It can: `IrRecord`, which `json_objectagg` already gives. A key built from data may repeat (two rows of one name, observed:
`Keys_built_from_data_may_repeat_and_are_kept_as_written`), but every value is one of those joined, so whichever a reader keeps
is one the type allows.

**Alternatives.**
- *Unknown, as for a key in a literal written twice.* That claims less than the server guarantees: a repeated key matters when
  its writes differ in type, and here they are all the type of the join.
- *Keys known by name beside keys from data, as an object with extra keys.* The IR has no such type, and the case is rare.
