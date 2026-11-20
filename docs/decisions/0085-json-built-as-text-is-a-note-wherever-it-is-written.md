# JSON built as text is a note wherever it is written

**Date:** 20 Nov 2026

**Decision.** Wherever the project builds a JSON array or object as text, in any module or query file, inference reports a
note, `HAR2025`, with the constructor that builds the same JSON: `json_arrayagg` for an aggregate, `json_array` for a fixed list,
`json_object` (or `FOR JSON PATH`) for an object, with `json_object` inside for an array of objects. It is found in the SQL
itself, by the construction, once at the outermost expression, whether or not anything reads the text as JSON and whatever
procedures are selected: a concatenation whose text is a JSON array or object, each part that is not a literal standing for a
value, for text in a string, or, where a key goes, for a key or members, and which shows it is JSON (a comma or a quotation mark
in its literals, a brace in an array's, `string_agg`, `concat_ws`, or a part that builds JSON); or `concat_ws` of quoted
literals. An object's own first brace shows nothing: `'{' + name + '}'` is a name in braces, not JSON. Where the SQL alone doesn't show it, such as brackets
around a list held in a variable, typing finds it where it reads the array, and gives the note there too, each place once.

**Context.** The idiom is old, and code that can be changed should be: the constructors escape every value, and haruspex types
what they build exactly. A real application's own sweep found 23 places beyond those that caused an unknown, because text that no
caller reads with `json_query` is rightly a string and raises nothing. An inventory for modernisation must find the construction,
not its effect on typing.

**Alternatives.**
- *Any concatenation that starts with `[` and ends with `]`.* T-SQL writes brackets of its own: `'[' + name + ']'` quotes an
  identifier, and diagnostic procedures write `'[' + n + ' KEYS]'`. On the public corpora that rule reported 18 places, 6 of them
  such brackets; the JSON evidence leaves the 12 that build JSON.
- *Report it only where it causes an unknown.* It would miss most of it, as the sweep showed.
- *Report it only in the procedures selected and what they call.* The inventory is of the source; a function no procedure calls
  is still code to change. A note is shown only with `--verbose`, so it is no noise elsewhere.
- *A warning.* A warning says the output rests on something unchecked, or that the input could be simpler, and one would be
  acknowledged in the config file for code that can't change. It is the maintainer's call; the note is provisional, because it
  changes nothing about the output and is shown only when asked for.
