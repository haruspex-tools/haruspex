# Haruspex.Emit.Zod

Writes a [haruspex IR](https://github.com/sharpblue/haruspex/blob/main/src/Haruspex.Ir/README.md) document as one TypeScript
module of [Zod 4](https://zod.dev) schemas: a schema and an inferred type for every named object type, every procedure's
results and every procedure's parameters.
It reads only the IR and knows nothing about SQL, which is what keeps it one emitter among several rather than the shape of
the IR.

## How data flows through it

Three stages, each with one job. The plan decides what the module declares, in what order and under what names, asking the
IR's type graph about references and cycles. The lowering turns each IR type into a small model of Zod expressions, records
that compare by value. The printer writes that model as TypeScript, and is the only part that knows TypeScript's syntax.
Why three stages, rather than text written as the IR is walked, is argued in the guide's chapter
[Emitting code](https://github.com/sharpblue/haruspex/blob/main/docs/internals/11-emitting-code.md).

```mermaid
flowchart LR
    document["IrDocument"] --> typeGraph["IrTypeGraph"]
    document --> plan["ZodPlanner: a ZodPlan"]
    typeGraph --> plan
    options["ZodOptions: strip or strict objects"] --> plan
    plan --> lower["ZodLowering, TypeLowering and ParameterLowering: a ZodModule"]
    lower --> print["ZodPrinter"]
    print --> module["one TypeScript module"]
```

## Example

```csharp
var document = IrJsonReader.Read(File.ReadAllText("haruspex.ir.json"));
var emitted = ZodEmitter.Emit(document, new ZodOptions { Objects = ZodObjects.Strip });
if (emitted is Succeeded<string> { Value: var module })
    File.WriteAllText("haruspex.zod.ts", module);
```

What a module looks like is shown, procedure by procedure, in
[the guide's first chapter](https://github.com/sharpblue/haruspex/blob/main/docs/internals/01-the-shape-of-the-problem.md).

## What it guarantees

**It claims everything the IR states, and nothing more**
([decision 0005](https://github.com/sharpblue/haruspex/blob/main/docs/decisions/0005-claim-everything-the-engine-guarantees.md)).
Each fact the IR carries becomes a Zod check: lengths, integer and decimal ranges, decimal steps (`.multipleOf`), exact
value sets as `z.enum` or `z.literal`, and whether a field may be null or absent. A fact the target can't hold exactly is
left out, and a note above the property says why, in one sentence; a fact many notes rest on is stated once, in the
module's header. For example, a `bigint` from a rowset arrives from the driver as a string, so its range can't be checked
as a number. A value set that is true only under the database's collation is a note, not an enum, because the value that
arrives may differ in case or trailing spaces. When the IR records the options the callers' sessions were declared to set
(`source.session`: `ansiWarnings`, `arithAbort`, `concatNullYieldsNull`, `numericRoundAbort`), the header says them too, since
the schemas are true only for callers that set them; with none declared, it says nothing, as nothing was assumed.

What a value looks like depends on how it arrives, and the module follows what actually arrives:

- **In JSON** (`FOR JSON` and the JSON functions), SQL Server writes dates, times and GUIDs as strings in fixed formats, so
  they are validated strings (`z.iso.date()`, `z.iso.datetime(…)`, `z.guid()`). They are deliberately not converted to
  `Date`: a `date` has no time zone, and `new Date('2026-11-09')` is UTC midnight, the previous day west of Greenwich.
- **In a rowset**, the types follow the default mapping of the [mssql](https://www.npmjs.com/package/mssql) (tedious)
  driver: dates and times as `Date`, `bigint` as a string.
- **A procedure's `FOR JSON` result** is the document, as the mssql driver hands it over with its `parseJSON` option: `null`
  when no rows matched, an array as much as an object, and an array that arrives has at least one item
  ([decision 0048](https://github.com/sharpblue/haruspex/blob/main/docs/decisions/0048-an-absent-json-result-reads-as-null.md)).
- **JSON that arrives as text** (a `FOR JSON` result that wasn't embedded) stays a `string`, since that is what arrives,
  and the check parses it and validates its content against the inner schema.
- **Recursive types** (a tree built by a function that calls itself) use Zod 4's getter form, so the inferred type needs
  no casts.
- **Nothing is said twice.** A shape the module would declare twice is declared once, and the second name is kept as an
  alias of the first, so code that imports either keeps working. A check the schema already makes is not repeated:
  `z.int32()` rather than an `int`'s own bounds, and a union without a variant another one already accepts.
- **It passes a linter.** A number a double can't hold is written as the double JavaScript reads it as, which is the check
  it always was, and the tests lint every fixture's module with ESLint's recommended rules and typescript-eslint's strict and stylistic sets, so
  every helper states its return type.

**A procedure's parameters** are a strict schema of what may be sent (`GetOrderParamsSchema`, and its type `GetOrderParams`),
declared before its results' schema, whatever the `objects` setting: a key it doesn't know is an error
([decision 0009](https://github.com/sharpblue/haruspex/blob/main/docs/decisions/0009-parameters-are-strict.md)). Each value is
checked against its declared type's facts, because SQL Server cuts a longer string short and rounds a decimal of more places
without an error
([decision 0054](https://github.com/sharpblue/haruspex/blob/main/docs/decisions/0054-a-parameter-is-checked-against-its-declared-type.md)):
a length in UTF-16 code units, an integer's and a decimal's range, a decimal's places. A date or time is an ISO 8601 string
of exactly the form its type holds, never a `Date`
([decision 0055](https://github.com/sharpblue/haruspex/blob/main/docs/decisions/0055-dates-and-times-are-sent-as-iso-8601-strings.md)):
a `Date` a rowset result handed over is sent back as its `toISOString()` without the `Z`, cut to the digits its type holds,
since a type without an offset would drop the `Z` without an error. A `bigint` is a safe integer, a JavaScript `bigint`, or the
string of digits a rowset hands over.
Every parameter accepts `null`, since a procedure can't declare one `NOT NULL`; one with a default, or an `OUTPUT` one, may be
left out. A table-valued parameter is `z.unknown()` for now, and a `json` one a string.

Objects strip unknown keys by default (`z.object`), so a database deployed ahead of its consumers can add a field without
breaking them
([decision 0008](https://github.com/sharpblue/haruspex/blob/main/docs/decisions/0008-objects-strip-unknown-keys-by-default.md)).
`ZodObjects.Strict` makes them `z.strictObject`. The output is deterministic, with stable ordering and LF line endings, so
a CI check can compare it byte for byte.

## The mapping helper

`MappingModule` holds `haruspex.mapping.ts`, which `"mapping"` in `"emit"` writes beside the module: TypeScript types, the same
for every module, with which an application's mapper accounts for every key of a row the module declares, at any depth, so a
key the row gains is a compile error in the mapper
([decision 0066](https://github.com/sharpblue/haruspex/blob/main/docs/decisions/0066-a-mapper-accounts-for-every-key-of-a-generated-row.md)).
It lives here because what it accounts for is this module's types: a `Date`, which a rowset's date arrives as, is a value
rather than an object with keys.

## What it deliberately doesn't do

- **No config file.** It takes `ZodOptions`. The CLI reads `haruspex.json` and maps its settings across, so the emitter
  works the same wherever it is composed.
- **No naming.** Names come from the IR, where the config's naming rules have already been applied. An IR name that isn't
  a TypeScript identifier is refused with a diagnostic (`ZodCodes`), not something the emitter quietly rewrites.
- **No SQL.** It can't look at a procedure, only at what the IR says about it. Every claim it makes is one the inference
  layer already proved.

It is part of [haruspex](https://github.com/sharpblue/haruspex), and pre-release: its API and its output will change. Its
chapter of [the guide](https://github.com/sharpblue/haruspex/blob/main/docs/internals/README.md) is
[Emitting code](https://github.com/sharpblue/haruspex/blob/main/docs/internals/11-emitting-code.md).
