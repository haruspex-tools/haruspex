# Diagnostics have stable codes, one type and one written form

**Date:** 13 Nov 2026

**Decision.** Every problem haruspex reports is a `Diagnostic`: a code, a message and, when it has a place, a location. The
code is `HAR` and four digits (`HAR1001`), and decides the severity: `error`, `warning` or `note`, the three levels of SARIF,
the format CI tools read. A location is one of three kinds, a closed hierarchy: a place in a SQL file (`SourceLocation`), a
value in a JSON document such as the config file (`JsonLocation`: the file, the path to the value, its line and column), or
a file or folder as a whole (`FileLocation`). One formatter, `DiagnosticFormatter`, writes every diagnostic as one line in the
compiler form, `file:line:column: error HAR1001: message`.

**The codes.** One code for each thing a user must do differently, not one per sentence: the shape errors of a JSON value
("must be a string", "must be an object") share one, and each rule with a reason and a remedy of its own has its own. Each
layer declares the codes of what it finds, in one class (`SourceCodes`, `InferenceCodes`, and the rest), because which
problems there are is the layer's knowledge and Diagnostics knows no layer. The first digit is the step a code was born in:

| Codes | Step |
|---|---|
| HAR1xxx | reading the SQL |
| HAR2xxx | inference: an unknown in the output, a note with one code for each of its reasons, and what inference could not check |
| HAR3xxx | reading the config file |
| HAR4xxx | applying the config file: selection, overrides, declared results, naming |
| HAR5xxx | emitting |
| HAR6xxx | the command line and files |

The first digit says where a code was born, not where its check is. A check that moves to another step keeps its code, and
nobody renumbers a code to tidy the ranges.

**Stable means:** a code's number, severity and meaning never change; its message's words may. A code no longer reported
is retired, not removed: it stays declared, marked `DiagnosticCode.Retired`, its page says so, and its number is never given out again.
`DiagnosticCodesTests` holds every code unique across the layers, which it finds by reflection over every project under
`src/`, and keeps the whole list (number, severity, title) as a committed snapshot, so a renumbered, reused or reclassified
code shows in a diff.

**Context.** A problem was reported three ways: a `Diagnostic` of message and source location for the SQL, a
config problem with a severity of its own for the config file, and plain strings for the command line's patterns, each
written to the console by a method of its own or by the CLI inline. None had a code, so a problem had no name to look up, to
suppress or to give a SARIF rule, and changing a message's words changed the only thing that identified it.

**Alternatives.**
- *No codes, as before.* The message is then the identity, so improving its words breaks whatever matched it, and there is
  nothing for a page of documentation to be named after.
- *A code per sentence.* About a hundred codes, many of them "must be a string" at a different path, with pages that would
  say the same thing. What a code names is a remedy.
- *`HX`*, which the build's own analysers use (`analysers/README.md`). Those are rules about haruspex's code, read by its
  contributors; these are about a user's SQL and config. One prefix for both would put two audiences in one list.
- *One catalogue of every code in Diagnostics.* Diagnostics would then know every other layer's problems, against its row
  in the layer table, and a layer's codes would live away from the checks that report them.
- *Locations as one record with nullable parts* (a file, maybe a path, maybe a line). Every reader would test which parts
  are there; a closed hierarchy names the three kinds, and the build checks each switch over them handles all three.
- *A location named for the config file.* Diagnostics knows no layer. What the config file's problems need is a place in a
  JSON document, which a declared schema read from another file needs too.
- *Unknowns as warnings.* A run with unknowns is a success: they are in the output, each with its reason. As warnings, a
  future "warnings are errors" setting would fail every run that has one. A note is what SARIF calls information that needs no
  action to succeed.

**Reason.** A problem has a name that outlives its wording, a page that explains it, and one written form that editors and
CI annotate, whichever step found it.

**See also:** `src/Haruspex.Diagnostics/README.md`; [decision 0037](0037-steps-return-outcomes.md), on how steps return them.
