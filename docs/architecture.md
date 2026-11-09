# Architecture: layers and what each may know

haruspex is split into layers so that each can be tested alone, reused alone and, where it is useful to
others, published alone as a NuGet package from this repository. Each project under `src/` is one layer.

```
.sql files ──► Sources ──► Model ──► Semantics ──► Inference ──► IR ──► emitters (Zod, …)
  (later .dacpac)                    (binding)                    ▲
                                                               Config (later)
              Diagnostics (locations and messages) is used by all of the SQL Server layers.
              The CLI composes everything.
```

## The table

| Layer (project) | Knows | Must not know | May refer to |
|---|---|---|---|
| `Haruspex.Diagnostics` | diagnostics: message and source location | anything else | nothing |
| `Haruspex.SqlServer.Model` | the schema as data: tables, columns, types, keys, procedures (bodies as ScriptDom fragments), views, synonyms and functions by name | where the schema came from; what any reference in a body points to | Diagnostics; ScriptDom |
| `Haruspex.SqlServer.Sources` | reading a folder of `.sql` files (later a DACPAC's `model.xml`) into the model | name resolution, types of expressions, the IR | Diagnostics, Model; ScriptDom |
| `Haruspex.SqlServer.Semantics` | binding a body's references to model objects (scopes, aliases, ambiguity), and interpreting the schema (which columns are never null) | result shapes, nullability inference, the IR | Diagnostics, Model; ScriptDom |
| `Haruspex.SqlServer.Inference` | shapes, nullability, presence and JSON structure, from bound bodies into the IR | files, zips, XML; the emitters; the config file | Diagnostics, Model, Semantics, Ir; ScriptDom |
| `Haruspex.Ir` | the IR model and its deterministic JSON | SQL of any dialect | nothing |
| `Haruspex.Config` *(not written yet)* | the config file: selection patterns, overrides, emitter settings | SQL syntax | Diagnostics, Model, Ir |
| `Haruspex.Emit.Zod` (later `Haruspex.Emit.JsonSchema`) | the IR and their own target | SQL, the model, the config file | Ir |
| `Haruspex.Cli` | composing all of the above | inference or emission logic of its own | every layer; System.CommandLine |

**The rule:** a layer refers only to what its row allows. In particular: ScriptDom is only for the four layers
that handle SQL syntax; the IR and the emitters never see SQL; Model, Semantics and Inference never read files,
zips or XML; and Semantics never sees the IR.

Why these lines:
- **Model and Sources are the "read a SQL Server project without DacFx" library** the SQL Server world lacks,
  useful to linters, diff tools and documentation generators. Nothing in them may lean on inference. They are the
  packable layers, with Diagnostics because `SourceLocation` is in their public API, and each has a `README.md`
  saying what it is for and what it deliberately doesn't do.
- **Semantics is separate from Inference** because binding is what every analysis needs (lint, lineage, diff),
  while inference is haruspex's own concern. Interpreting the schema belongs with binding, so other tools get
  "this column is never null" without running inference.
- **Emitters see only the IR.** If an emitter needs something, the IR must carry it.

## Enforced by a test

The project references are the first enforcement. `tests/Haruspex.Tests/ArchitectureTests.cs` is the second: it
holds the table above as data and fails when

- a project under `src/` is not a layer in the table, or a layer that has code today has no project;
- a compiled assembly refers to a layer, or a package's assembly, that its row doesn't allow (read from the
  assembly's metadata, so it is what the code really uses);
- a project file has a `ProjectReference` or `PackageReference` its row doesn't allow (what a published package
  would depend on, even if unused);
- Model, Semantics or Inference refers to the framework's XML, compression or packaging assemblies, or uses the
  file system's types (`File`, `Directory`, `FileInfo`, `DirectoryInfo`, `FileStream`, `FileSystemInfo`). Those
  types share `System.Runtime` with everything else, so the test reads each assembly's type references for them.

Adding a layer means adding it to the table here and in the test, in the same change. Dependency direction
erodes one convenient reference at a time, which is why it is checked rather than only written down.

## Types that were split, and why

Slice 1 put reading, the catalogue, binding and inference in one project, `Haruspex.SqlServer`. Slice 2b split it
without changing behaviour (every snapshot and CLI output is byte-identical). Where one type mixed two concerns,
the type was split rather than moved whole:

| Was | Became | Why |
|---|---|---|
| `SqlProject` (read a folder, select, infer) | `SqlScriptFolder` (Sources), `DatabaseModel` (Model), `IrInference` (Inference), and `SqlProject` kept as the composition in the CLI | Reading and inferring are different layers, and no layer below the CLI may know both. Tests and the differential harness still use `SqlProject`, now from `Haruspex.Cli`. |
| `Catalog`, `TableModel`, `ColumnModel` (tables with each column's IR shape and a proved-non-null flag) | `Table`, `Column`, `DeclaredNullability` (Model); `SchemaRules.IsNeverNull` (Semantics); the column's shape in `ProcedureAnalyzer` (Inference) | The model holds declarations (NULL / NOT NULL, primary key, IDENTITY, computed expression). That NOT NULL, a key column or an identity column is never null is an interpretation of the schema, so it moved to Semantics. Mapping a column to an IR shape is inference. |
| `SqlTypes.ToShape(DataTypeReference)` | `SqlType` (Model: kind, canonical name, arguments as written); `SqlTypes.ToShape(SqlType)` (Inference) | What a type *is* is schema data; what it becomes in the IR is inference's choice. Lower-casing the arguments for the IR's SQL type text stays in Inference. |
| `ProcedureAnalyzer` (scopes, sources, aliases, column resolution, and shapes) | `Binder`, `BindingScope`, `BoundSource`, `ColumnBinding`, `BindingProblem` (Semantics); the rest of `ProcedureAnalyzer` (Inference) | Binding needs no IR. A binding problem has its own kind, which Inference maps to the IR's unknown reason. An outer join's optional side is recorded as structure (`BoundSource.OptionalSideOf`); that it makes a value nullable is decided in Inference. |
| `UnknownShape` built by `SqlProject.Changed` for ALTER/DROP | `Redefinition` (Model), turned into an unknown by Semantics (tables) and Inference (procedures) | The doubt about which definition is in effect is a fact about the source; how it surfaces in the IR is inference's. |
| `Diagnostic`, `SourceLocation` | moved whole to `Haruspex.Diagnostics` | Already one concern. The design gives a diagnostic a code and severity too; nothing needs them yet, so they were not added. |
| `ObjectName` | moved whole to Model | Names are schema data. |
| `ObjectPattern` | moved whole to the CLI | Selection patterns are the config file's concern (the design gives them to `Haruspex.Config`), not schema data, so they stay out of the publishable Model. Until Config exists the CLI holds them; they move to Config with it. |

Two behaviours were kept although a cleaner split would change them, because this slice changes no behaviour:
RIGHT and FULL joins, CROSS JOIN and APPLY are left unbound by the binder (binding them is easy, but what they
mean for nulls is not modelled, and binding them would change which unknowns are reported); and a table changed
by `ALTER TABLE` is still reported with the reason `unsupported-source`.
