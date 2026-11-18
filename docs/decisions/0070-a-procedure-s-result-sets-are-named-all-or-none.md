# A procedure's result sets are named all or none

**Date:** 18 Nov 2026

**Decision.** A procedure's entry in `procedures` may name its result sets, in order: `"sets": ["order", "lines"]`. Its function
then returns `{ order, lines }`, with `output` and `returnValue` beside them as before, **even when there is one set**, and its
types are named after the sets (`GetOrderLinesRow`, not `GetOrderResult2Row`), a `name` in `paths` still winning. Every set the
procedure is known to return is named, or none is: a list of more or fewer names is an error naming both counts
([HAR4112](../diagnostics/HAR4112.md)), and so is a list for results that can't be listed. Each name is in camel case, unique,
not a reserved word, and neither `output` nor `returnValue` ([HAR3023](../diagnostics/HAR3023.md)). The names are recorded in
the IR, as each result's `name` (0.23), so the schemas and the functions are made from the IR alone, and `haruspex check`
reports a renamed set. The schema module declares named sets as an object of them (`z.object({ order, lines })`, and an object
with a property for each in JSON Schema), where unnamed sets are a tuple. A query file names its sets with a comment, `-- @sets
order, lines`, under the same rules, each problem reported at the comment, since a query file's own settings are comments
already (`-- @param`) and `procedures` takes only a procedure's name. Richard accepted the design on 18 Nov 2026.

**Context.** A trial of the npm preview on a real application found several procedures whose sets came back as `result1`,
`result2`, which say nothing about what they hold. And under [decision 0060](0060-a-call-returns-its-one-result-itself.md) a
procedure of one set returns it alone, so adding a second set changed the function's shape: every caller had to change, though
the first set was the same. A set a newer database adds after the known ones is already ignored
([decision 0063](0063-a-function-ignores-result-sets-after-those-it-knows.md)).

**Alternatives.**
- *A key per set in `paths`* (`"results[1]": { "key": "lines" }`). It reuses an existing setting, but is harder to read, and
  whether every set is named is hard to see.
- *Some sets named, the rest numbered.* Flexible, but one object would mix two conventions, and a name could silently fill a
  numbered slot when a set is added before it.
- *A named object only for several sets*, and the set itself for one. That keeps decision 0060's flip, which naming is meant to
  remove.

**Reason.** A name says what a set holds, and the same name in the object, the type and the IR keeps them in step. Naming
every set, even one, is how a team says a procedure will grow: its function already returns an object, so a set added later
is one more key, and the types of the sets before it keep their names. All or none keeps the object regular, and the count
check catches a list left behind when a set is added or removed, since a set added in the middle would otherwise take
another's name. The names are keys the function binds beside its own, which is why they are camel case and can't be a
reserved word or a key the function already returns.

**In the code:** `ConfigReader` (reading `sets`), `QueryFileReader` (reading `-- @sets`), `SetNameRules`, `ConfigApplying.NameSets`, `NameSettler` (the type names), `IrResult.Name`,
`ZodLowering.Procedure`, `JsonSchemaLowering.Procedure` and `MssqlFunction.NamesSets` with `MssqlPrinter.Fields`.
