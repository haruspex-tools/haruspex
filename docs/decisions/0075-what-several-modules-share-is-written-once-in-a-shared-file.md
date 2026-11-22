# What several modules share is written once in a shared file

**Date:** 18 Nov 2026

**Decision.** In output split into modules ([decision 0074](0074-a-config-may-split-its-output-into-modules.md)), a definition
two or more modules use is written once, in a shared file per emitter, which each module imports from and which imports nothing
of theirs; a definition one module uses stays in it. Which definitions are shared is decided on the whole document, as for one
module, so every name and every shape is what the unsplit output gives.

- **Zod**, `shared.zod.ts`: a named type the procedures of two or more modules reach, and a shape several types share whose
  types are reached from two or more modules ([decision 0071](0071-a-shared-shape-is-named-by-declaration-or-by-its-structure.md)).
  A shape under a declared name stays a public export, now from `shared.zod.ts`. A shape named by its structure
  (`shape3f2a9c`) must be exported there for the modules to import it, so it is exported with a `/** @internal */` comment and
  no type, and no module re-exports it: it is **not one of the public names**, and may change whenever the shapes do. Each
  module exports its own types under their own names, as `export const ListStaffRowSchema = shape579045`.
- **JSON Schema**, `shared.jsonschema.json`: a named type the procedures of two or more modules reach, defined in its `$defs`,
  which a module refers to by a `$ref` relative to its own document's name, `shared.jsonschema.json#/$defs/PartNode`. Shapes
  aren't shared in JSON Schema (decision 0071), so this holds only types that are the same type, such as a recursive function's.
  A consumer adds each document under its file name (`ajv.addSchema(document, "sales.jsonschema.json")`) and asks for a
  definition by `sales.jsonschema.json#/$defs/GetOrder`.
- **The functions** share nothing: each module's `sales.mssql.ts` is the file one config writes for its procedures, with its own
  prelude (the `Queryable` interface, the two error classes and the helpers), and imports its own module's Zod file.
- A shared file is written only when something is shared.

**Context.** Two modules can't import each other: that would make the order of modules matter and a change in one break another.
And a shape a trial application's procedures shared was shared 123 times; writing it into each module would undo what decision
0071 saved.

**Alternatives.**
- *Share shapes only within a module*, and the shared file only for types that are one type. Then a module changes only when
  its own schema does, but the same `{ total }` would be written once per module, and the shape another module declared by name
  could not be shared across them.
- *Every structural shape exported from the shared file as a public name.* Its name is a hash, which no application should
  write; marked internal, a team's API tools and `stripInternal` leave it out.
- *A shared file of the functions' prelude* (`shared.mssql.ts`), so that one `ParameterError` class is thrown by every module and
  `instanceof` holds across them. The prelude is the same in every module and decision 0056 already tells errors apart by name
  and kind, as an application with several configs does; whether to add one more shared file is left for the maintainer to decide.
- *In JSON Schema, an `$id` in each document*, so that references resolve wherever the documents are loaded from. An `$id` is a
  URI, and any value would name a place haruspex doesn't know; a relative `$ref` resolves against the name a consumer gives the
  document, which is its file's.

**Reason.** Every definition is written once and every name is the one the unsplit output gives, so the split moves text
between files and changes no claim. A procedure added to one schema changes its module's files alone, unless it shares a shape
with another module, which then imports the shape from the shared file: its own types keep their names, and its file gains an
import. The shared file says what it is in its header.

**In the code:** `IrModules.HomeOf`, `ZodSplit` with `ZodLowering.Placed`, `JsonSchemaLowering.Lower(plan, modules)`, and
`MssqlEmitter.Emit` with `IrModules`.
