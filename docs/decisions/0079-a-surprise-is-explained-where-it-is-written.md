# A surprise is explained where it is written

**Date:** 19 Nov 2026

**Decision.** Generated code says why a field is what it is when that is a surprise: a field whose nullability, presence or type
differs from what its source column declares (a reason beside a nullable column, a parameter or a declaration; a key left out
whatever its value; a scalar of a type none of its columns has), and every unknown (a key left out when null: see the amendment
below). The comment gives each reason's short text and code,
the way out when there is one (`COUNT_BIG`, a `"nullable": false` override with its reason), and the `haruspex explain`
command that shows the rest. `"comments"` in the config file chooses `"none"`, `"surprises"` (the default) or `"all"`. The Zod
and mssql modules write comments; JSON Schema writes the same lines in the property's `description`. The notes that state a
fact a check rests on are not these, and are written whatever the setting.

The codes are the IR's own names (the null rules, the unknown reasons and the cardinalities): no new numbers. Each has a short
text (`IrReasons`), a page under [`docs/reasons`](../reasons/README.md), and the test it rests on, named on that page;
`ReasonCatalogueTests` fails for a code without all three. The codes that start with HAR stay for what a run reports.

A comment leaves out where each rule applied (a file and line), which `explain` prints, so that it changes only when the
reason does. A shape several types share is compared without the comments, so they never stop it being shared; a type
exported as such a shape says its own fields' reasons above its export.

**Context.** A deliberate widening, a `COUNT` that may be null ([decision 0014](0014-count-is-nullable.md)) or a key absent
when null, looks like a bug to someone reading the generated type, and the reason was in the IR, which nobody reads. The
reader of the generated file is the person who needs it, at the moment they meet the type.

**Alternatives.**
- *Comment every field by default.* Most fields are what their column says, and a comment on each hides the few that aren't.
  `"all"` is there for a team that wants it.
- *New numbered codes for reasons*, like the diagnostics'. A second name for each thing the IR already names stably, which a
  tool would have to map back.
- *Locations in the comment.* Exact, but every edit above a procedure's query would change every comment below it, and a
  review of generated code would be noise.
- *Let comments split shared shapes.* Shapes are shared by structure, and a large application shares one shape over a hundred
  times ([decision 0071](0071-a-shared-shape-is-named-by-declaration-or-by-its-structure.md)); the comments would undo it.

**Reason.** The reason belongs where the surprise is met. The default changes every user's generated code once, before 1.0,
for comments on the fields they would otherwise ask about.

**Amended** (19 Nov 2026, Richard). A key left out of `FOR JSON` output when null is a surprise only through a reason the
declarations don't give: a NOT NULL column made nullable by an outer join, an aggregate, a subquery or any other rule, whose
comment says why it may be null and that the key is then left out. A key left out only because its column, a parameter or the
config file says it may be null is not one, since that is what `FOR JSON` does with every null; the Zod module's header says so
once, for every such key, and JSON Schema's description of each property says it already. A key left out whatever its value, in
a combined document, still is. `"all"` still comments every field.

On a large application's procedures, such keys were nearly half the fields the default explained, and their comments more than
doubled the Zod module, each repeating what the type already says: `.optional()` on a nullable column's key is that column
as declared, through a rule the header can state once. A comment on every field that is as its column declares is what the
first alternative above rejects, and these were that.

**In the code:** `IrReasons`, `IrSurprises`, `IrFieldText`, `IrFieldPlaces`, `IrComments`; `ZodOptions.Comments`,
`MssqlOptions.Comments`, `JsonSchemaOptions.Comments`; `Explain` in the CLI; `ReasonCatalogueTests`.
