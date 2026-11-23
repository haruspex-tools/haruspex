# JSON assembled as text is typed where each element is one JSON value

**Date:** 20 Nov 2026

**Decision.** A JSON array built by joining text (`'[' + string_agg(…) + ']'`, `concat`, `concat_ws`) is typed where it is read
as JSON, and only where the server guarantees its shape: each element's text must be exactly one JSON value (a literal, a
number converted to text in a form that writes its value, live JSON, one row of `FOR JSON … WITHOUT_ARRAY_WRAPPER`) or a string
in quotes whose text can't hold a quotation mark, a backslash or a control character (`string_escape(x, 'json')`, a number, a
date or a `uniqueidentifier` converted to text, a value whose every value is known and holds none, a literal). Anything else in
the brackets makes the JSON unknown, naming it. What a string's text is made of is a fact of its value (`Value.Pieces`), stated by
the rules that build strings and dropped by every other, and read where the text is read as JSON (`JsonAssembly`). Read without
`json_query`, the text stays a string.

**Context.** A trial on a real application found this idiom, from before `json_arrayagg` and `json_array`, behind many of its
unknowns. The first plan said a value in quotes without `string_escape` was "still a string", since a value that broke the
JSON would make the read fail. Observation said otherwise (`An_unescaped_value_in_quotes_can_make_more_elements_a_number_or_cut_the_array_short`):
`json_query` reads only as far as the first value, so `a"]x` ends the array early and the read succeeds, and `a","b` and
`a",1,"b` make arrays of another shape that are valid JSON. Each of the other rules was observed too
(`JsonAssembledObservationTests`): a NULL aggregate in `+`, in `concat` and in quotes; `money` rounded or given commas by a
style; `float` cut to six digits; a NULL separator; a fragment of many rows.

**Alternatives.**
- *Type a value in quotes as a string whatever it holds.* Rejected: the read succeeds with another shape, which is the one thing
  haruspex must never claim.
- *Match whole expressions syntactically where they are read.* The forms nest through scalar subqueries, `COALESCE`, `CASE`,
  conversions, variables and functions; each would need the matcher again, and an expression would be typed once as a string and
  again as text. A fact of the value composes through every rule that keeps the text, and a rule that forgets it claims nothing.
- *Make the text JSON text (`json-text`) wherever it is.* It would change the type of every column that returns such text, which
  an application already reads as a string, and the change would ask it to parse what it never asked for.
- *Leave it unknown and only report it.* The idiom is in code that can't always be changed, and its shape is often guaranteed.
