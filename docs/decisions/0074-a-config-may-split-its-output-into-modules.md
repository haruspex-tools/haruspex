# A config may split its output into modules

**Date:** 18 Nov 2026

**Decision.** `"split"` in `haruspex.json` splits what `generate` writes into modules: `"split": "schema"` gives a module per SQL
schema of the selected procedures, named as the schema is, and `"split": { "sales": ["sales", "shop"], "admin": ["admin"] }`
gives the modules listed, each holding the procedures of its schemas. A selected procedure whose schema no module lists is an
error naming it ([HAR4008](../diagnostics/HAR4008.md)), and so is a schema listed twice ([HAR3025](../diagnostics/HAR3025.md)).
A query file is in the module its `-- @module` comment names, or else in `queries`; the comment must name one of the modules,
or `queries`, and does nothing, with a warning, where the output isn't split ([HAR4009](../diagnostics/HAR4009.md)). Each module
is the files one config writes, named after the module: `sales.zod.ts`, `sales.mssql.ts`, `sales.jsonschema.json`. A module's
name starts with a letter and has only letters, digits, `_` and `-`, since it names files; `shared` is the shared files' name
([decision 0075](0075-what-several-modules-share-is-written-once-in-a-shared-file.md)), and no two modules' names differ only in
case ([HAR3024](../diagnostics/HAR3024.md)). The IR stays one file, `haruspex.ir.json`, recording no module, and so does the
mapping helper, the same for every module. Every exported name is the one the unsplit output gives, so switching the split on or
moving a schema between modules renames nothing. There is no `index.ts` that re-exports the modules. Without `split`, every file
is byte for byte what it was. The top-level session recommended this design on 18 Nov 2026, without a questions round; it is
provisional until Richard reads it.

**Context.** Every reader of a generated module imports all of it: on a real application's trial, 585 KB of Zod for 216
procedures. A reviewer of a generated diff wants the change in the file of the schema that changed. Several config files already
give several modules, sharing nothing, for parts of an application that are apart; one config split into modules is for one
application whose output is too big to read as one file.

**Alternatives.**
- *Several config files*, one per schema. Each repeats the source, the naming and the session; a type two of them share is
  written twice under two names; and a name is unique only within its config, so the same procedure's types could clash
  between two modules.
- *An `index.ts` re-exporting every module.* An application importing it imports everything again, which undoes the split.
- *Query files in the module of the schema they read most.* A query file reads what it likes, so that is a guess; a comment
  says it, as `-- @sets` and `-- @param` say a query file's other settings.
- *The default module for query files named for the config* (`haruspex`), or required to be named. `queries` says what it
  holds; requiring a comment in every query file of a split config is noise for an application that keeps them together.
- *A module per schema only.* Schemas are often too many and too small (a schema per team), or too few; a list groups them
  without renaming anything.
- *Per-module IR files.* `check` and `check --baseline` compare claims, not files, and the IR is the contract a database's
  pipeline reads; one document keeps them as they are.

**Reason.** A module is a way of writing the document out, not a claim about the database, so the IR doesn't change and neither
does any name: the split is a layout, and can be changed freely. Names unique across the whole config are what make that so.
A module's name is a file name on every platform, so it is held to what every file system takes. An object in no module is an
error, since leaving it out would drop an interface without a word, as an include that matches nothing would.

**In the code:** `ModuleSplit` and `ModuleAssignment` (Config), `QueryFileReader` (the comment), `IrModules` (Ir), `ZodEmitter.Emit`,
`JsonSchemaEmitter.Emit` and `MssqlEmitter.Emit` with `IrModules`, and the CLI's `SplitModules`.
