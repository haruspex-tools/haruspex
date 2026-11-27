# The IR's JSON Schema, one file per version

Each file here is the JSON Schema of an IR file, `haruspex.ir.json`, named for the IR version whose reader it describes
(`0.35.schema.json`). Its `$id` is its own address, `https://haruspex.tools/schemas/ir/<version>.schema.json`,
so a file checked against it is checked against what that version's reader reads, whatever `main` holds later. A schema lists
in `irVersion` every version its reader reads: the current one, and the older ones whose documents mean the same
([decision 0024](../../docs/decisions/0024-an-ir-reader-reads-only-versions-whose-documents-mean-what-its-own-do.md)).

- **The current version's file** is generated from the model by `IrJsonSchema` and never edited by hand
  ([decision 0023](../../docs/decisions/0023-the-ir-schema-is-generated-from-the-model.md)). `IrJsonSchemaTests` fails when it
  is not what the generator writes; rewrite it with `HARUSPEX_ACCEPT=1 dotnet test --filter IrJsonSchemaTests`. A correction
  within a version (a rule the schema could state and didn't) changes it in place, and only ever refuses what that version's
  reader already refused.
- **An older version's file** stays as it was when its version was current. The generator writes only the current version's,
  so when the version moves on, the file of the version left is kept and a new one is written beside it, as
  [`tests/ir-versions`](../../tests/ir-versions/README.md) keeps a file of every version. `IrJsonSchemaTests` checks that each
  still validates that version's file there. The first version kept this way is 0.29. Only its `$id` has changed since: every
  file's moved from GitHub to haruspex.tools together, because the site serves each of them and an `$id` is where a file is
  found, not a rule it states ([decision 0121](../../docs/decisions/0121-every-address-haruspex-writes-is-served-from-haruspex-tools.md)).

To check a file, use the schema of the version of haruspex that will read it: a program that pins a release checks against
that release's version. The format itself is specified in [the IR README](../../src/Haruspex.Ir/README.md).
