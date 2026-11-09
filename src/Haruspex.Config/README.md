# Haruspex.Config

The config file, `haruspex.json`: reading it, selecting procedures, and applying overrides and naming to the IR. The
format is documented for users in [docs/configuration.md](../../docs/configuration.md) and published as a JSON Schema in
[schemas/haruspex.schema.json](../../schemas/haruspex.schema.json).

It knows the IR and the model's object names, and **not SQL syntax** (no ScriptDom), as the layer table in
[docs/architecture.md](../../docs/architecture.md) says. It does not know the emitters: the CLI maps `objects` to the Zod
emitter's own options.

| File | What it does, and why it is so |
|---|---|
| `ConfigJson.cs` | Reads JSON with comments into a tree that remembers each value's line and column. `JsonDocument` forgets positions, and every error must name its line. |
| `ConfigReader.cs` | Finds the file (walking up) and checks every setting; an unknown key is an error, never ignored. The same checks as the JSON Schema, plus what a schema can't say (a pattern, a path or a regular expression that doesn't parse, a reserved word). |
| `NamePattern.cs`, `ObjectPattern.cs`, `ProcedureSelection.cs` | Selection per schema (`schemas["sales"].include`, matching names only), and the CLI's one-off `--include schema.pattern`. A pattern or a schema key that matches nothing is reported. |
| `OverrideApplier.cs` | Path overrides as transformations of the IR. Each claim is listed in the IR's `overrides`; a path that doesn't exist is an error, an override the inference agrees with a warning. |
| `NamingRules.cs` | `replace`, then the schema's `prefix`, then `case`; a schema's `naming` merges over the top level's. |
| `NameSettler.cs` | Settles every name, replacing inference's provisional keys. Clashes between objects are errors; a number is added only between two types of one object. |
| `ConfigApplication.cs` | The order: overrides first (their paths are in terms of the inferred document), then names. |

**Why naming lives here, not in inference.** Names are generated from config, so they can't be settled before it is
read. Inference keys each type by its identity (the object and the path that own it, also written to the IR as
`identity`), and leaves each procedure's name as its id; this layer replaces both. Before slice 4 inference settled
names itself and broke clashes with numbers, which made names depend on which other objects existed.

**Why overrides come before naming.** A path override is written in terms of the shape, which doesn't depend on names,
and a `name` override must be known before the types under it are named, since their names build on it.
