# Architecture: layers and what each may know

The code is split into layers so that each can be tested alone, reused alone and, where it is useful to
others, published alone as a NuGet package from this repository. Each project under `src/` is one layer.

```
.sql files, .sqlproj, .dacpac ──► Sources ──► Model ──► Semantics ──► Inference ──► IR ──► Config ──► IR ──► emitters (Zod, JSON Schema, mssql)
                                                        (binding)                         (overrides,       (names settled)
                                                                                            naming)
              Syntax (ScriptDom's declarations read into the model) is shared by Sources, Semantics and Inference.
              Diagnostics (codes, messages, locations and the outcome of a step) is used by every layer but Collections and the IR.
              Collections (lists that compare by value) is used by the layers whose records hold them.
              The CLI composes everything.
```

## The table

| Layer (project) | Knows | Must not know | May refer to | Files, zips, XML |
|---|---|---|---|---|
| `Haruspex.Collections` | immutable collections that compare by value | anything else | nothing | never |
| `Haruspex.Diagnostics` | diagnostics: a stable code, a message and where (a place in a source file, a value in a JSON document, a file); the outcome of a step, a value or why there is none | anything else | Collections | may |
| `Haruspex.SqlServer.Model` | the schema as data: tables, columns, types, collations, keys, CHECK constraints, alias and CLR types, procedures, functions, views and triggers (their bodies and expressions as ScriptDom fragments, nothing else of ScriptDom), synonyms; an application's query files, read against it | where the schema came from; how a declaration is parsed; what any reference in a body points to | Collections, Diagnostics; `Microsoft.SqlServer.TransactSql.ScriptDom` | never |
| `Haruspex.SqlServer.Syntax` | reading what ScriptDom parsed (a name, a type, a table definition, a procedure, function, view, trigger or synonym) into the model's values, wherever it is written: in a script or in a body | files; which object a reference means; types of expressions, the IR | Collections, Diagnostics, Model; `Microsoft.SqlServer.TransactSql.ScriptDom` | never |
| `Haruspex.SqlServer.Sources` | reading a folder of `.sql` files, a database project's `.sqlproj` as its build would, or a DACPAC's `model.xml`, with the projects and DACPACs they refer to, into the model | name resolution, types of expressions, the IR | Collections, Diagnostics, Model, Syntax; `Microsoft.SqlServer.TransactSql.ScriptDom` | may |
| `Haruspex.SqlServer.Semantics` | binding a body's references to model objects (scopes, aliases, ambiguity), and interpreting the schema (which columns are never null; what types, alias types and CHECK constraints guarantee about values); SQL Server's built-in types, conversions and functions, as catalogues | result shapes, nullability inference, the IR | Collections, Diagnostics, Model, Syntax; `Microsoft.SqlServer.TransactSql.ScriptDom` | never |
| `Haruspex.SqlServer.Inference` | shapes, nullability, presence and JSON structure, from bound bodies into the IR | files, zips, XML; the emitters; the config file | Collections, Diagnostics, Model, Syntax, Semantics, Ir; `Microsoft.SqlServer.TransactSql.ScriptDom` | never |
| `Haruspex.Ir` | the IR model and its deterministic JSON | SQL of any dialect | Collections | may |
| `Haruspex.Config` | the config file: selection patterns, overrides as transformations of the IR, naming rules, emitter settings | SQL syntax | Collections, Diagnostics, Model, Ir | may |
| `Haruspex.Emit.Zod` | the IR and its own target | SQL, the model, the config file | Collections, Diagnostics, Ir | may |
| `Haruspex.Emit.JsonSchema` | the IR and its own target | SQL, the model, the config file | Collections, Diagnostics, Ir | may |
| `Haruspex.Emit.Mssql` | the IR, its own target (functions over the mssql driver) and the names a schema module declares | SQL, the model, the config file, the other emitters | Collections, Diagnostics, Ir | may |
| `Haruspex.Cli` | composing all of the above | inference or emission logic of its own | every layer; `System.CommandLine` | may |

This table is the one copy: `ArchitectureTests` reads it (below), and every other mention of the layers points here. In
"May refer to", a layer is named without `Haruspex.` and `SqlServer.`, and packages, in backticks, follow the semicolon.

**The rule:** a layer refers only to what its row allows. In particular: ScriptDom is only for the five layers
that handle SQL syntax; the IR and the emitters never see SQL; Model, Syntax, Semantics and Inference never read files,
zips or XML; and Semantics never sees the IR.

Why these lines:
- **Model, Syntax and Sources are the "read a SQL Server project without DacFx" library** the SQL Server world lacks,
  useful to linters, diff tools and documentation generators. Nothing in them may lean on inference. They are the
  packable layers, with Diagnostics because `SourceLocation` is in their public API (and Collections, because the model's
  records hold its lists), and each has a `README.md`
  saying what it is for and what it deliberately doesn't do.
- **Syntax is apart from Model and from Sources** because reading ScriptDom's syntax is needed in two places: Sources
  reads a script's declarations with it, and binding and inference read a body's own (`CREATE TABLE #t`,
  `DECLARE @t TABLE`), which must mean what the same declaration means in the schema. In Model it would put ScriptDom's
  types in the model's public surface; in Sources, binding would depend on reading files.
- **Semantics is separate from Inference** because binding is what every analysis needs (lint, lineage, diff),
  while inference is haruspex's own concern. Interpreting the schema belongs with binding, so other tools get
  "this column is never null" and "this column takes `open`, `held` or `closed`" without running inference
  (`SchemaRules.ColumnFacts`, `CheckReader`). Inference maps those facts into the IR; Semantics never sees it.
- **Inside Inference, the typer is apart from the walker.** What an expression or a query gives is a compositional
  semantics (`Typer`), a function of the expression, its `ReadScope` and readonly inputs; the state from statement to
  statement, its joins and fixed points are a flow-sensitive one (`Walker`). The typer holds no state and reads none of the
  walker's, which `TyperAndWalkerTests` reads the compiled code for (a query file's first walk hands the typer a sink, `ParameterUses`, which it only adds to); summaries are reached through one root (`Summaries`)
  passed to each body read ([the guide](internals/04-abstract-interpretation.md),
  [decision 0035](decisions/0035-the-typer-apart-from-the-walker.md)).
- **Emitters see only the IR, and not each other.** If an emitter needs something, the IR must carry it. The mssql emitter's
  functions import what a schema module declares, by the names `SchemaModuleNames` gives, which the Zod emitter declares; the
  two meet in the generated TypeScript, where the emit check compiles them together, not in a reference between the layers, so
  a second schema emitter can serve the same functions. The IR keeps each value's SQL type as
  written (`IrScalar.Sql`), and the Zod emitter reads it in one place: a date and time parameter's fraction and range, which
  the IR has no fact for (`DateTimes`, [decision 0055](decisions/0055-dates-and-times-are-sent-as-iso-8601-strings.md)). It
  reads a type's name from a closed list, never a statement.
- **Config transforms the IR; it never reaches into inference.** Overrides and naming apply to the document inference
  produced, so they work the same however it was produced, and an emitter never needs to know they existed. Inference
  leaves names unsettled (each object's id, each type keyed by its identity); Config settles them. The emitter is
  handed its settings (`ZodOptions`) by the CLI and never reads the config file. The other way, the config is handed the words the
  generated code's language reserves (`ReservedWords`), which no name it settles may be: which words those are is the
  emitter's knowledge (`TypeScriptNames`), never the IR's.
- **Declared results are an input to inference, in the IR's terms.** A procedure's declared results stand in
  for a call of it inside another, and a call's result sets are placed among its caller's by the statement walk, through
  branches and loops; that can't be done to a finished document. So Config reads the declaration into IR results, the CLI
  hands them to `IrInference.Infer` as `DeclaredResults`, as it hands it the model, and Inference never reads the config
  file. A procedure's own declaration is applied by Config to the document, like any override, and checked there against
  what is inferred. A referenced DACPAC, later, is the same kind of input: facts about objects outside the project.

## Each layer's README

Each project has a README that is its layer's chapter in brief: what it is for, its main types, an example, what it
guarantees and what it deliberately doesn't do.

- [`Haruspex.Collections`](../src/Haruspex.Collections/README.md)
- [`Haruspex.Diagnostics`](../src/Haruspex.Diagnostics/README.md)
- [`Haruspex.SqlServer.Model`](../src/Haruspex.SqlServer.Model/README.md)
- [`Haruspex.SqlServer.Syntax`](../src/Haruspex.SqlServer.Syntax/README.md)
- [`Haruspex.SqlServer.Sources`](../src/Haruspex.SqlServer.Sources/README.md)
- [`Haruspex.SqlServer.Semantics`](../src/Haruspex.SqlServer.Semantics/README.md)
- [`Haruspex.SqlServer.Inference`](../src/Haruspex.SqlServer.Inference/README.md)
- [`Haruspex.Ir`](../src/Haruspex.Ir/README.md), which also describes the IR's format
- [`Haruspex.Config`](../src/Haruspex.Config/README.md)
- [`Haruspex.Emit.Zod`](../src/Haruspex.Emit.Zod/README.md)
- [`Haruspex.Emit.JsonSchema`](../src/Haruspex.Emit.JsonSchema/README.md)
- [`Haruspex.Emit.Mssql`](../src/Haruspex.Emit.Mssql/README.md)
- [`Haruspex.Cli`](../src/Haruspex.Cli/README.md)

The analyser that checks haruspex's own code at build time is not a layer, and has [a README of its own](../analysers/README.md).

## Enforced by a test

The project references are the first enforcement. `tests/Haruspex.Tests/Architecture/ArchitectureTests.cs` is the second: it
reads the table above, from this file, and fails when

- a project under `src/` is not a layer in the table, or a layer that has code today has no project;
- a compiled assembly refers to a layer, or a package's assembly, that its row doesn't allow (read from the
  assembly's metadata, so it is what the code really uses);
- a project file has a `ProjectReference` or `PackageReference` its row doesn't allow (what a published package
  would depend on, even if unused);
- a layer whose row says it never handles files, zips or XML (Model, Syntax, Semantics and Inference) refers to the framework's
  XML, compression or packaging assemblies, or uses the file system's types (`File`, `Directory`, `FileInfo`,
  `DirectoryInfo`, `FileStream`, `FileSystemInfo`). Those types share `System.Runtime` with everything else, so the test
  reads each assembly's type references for them;
- the table names a layer it doesn't list, or a cell the test can't read, so a typo can't allow nothing silently.

**Why the test reads the document**, rather than holding the table as data and writing the document from it: the
table is a design decision, read and argued over as prose, and the reasons for its lines are beside it here. Kept in
the document, a change to the layering is one edit that a reviewer reads where the reasons are, and the test makes sure
the code obeys it. Generated from the test's data, the document would be an output that someone must remember to
regenerate, and the decision would live in a C# array that few readers open. The cost is a small parser, which fails
loudly on a cell it can't read.

Adding a layer means adding its row here. A layer decided on but without code yet is marked `(planned)` after its name
(none is today), so its project may be missing; when its code arrives the mark is removed, and the
project must then exist. Dependency direction erodes one convenient reference at a time, which is why it is checked
rather than only written down.

## Types that were split, and why

The SQL Server code began as one project, `Haruspex.SqlServer`, holding reading, the catalogue, binding and inference.
It was split into the layers above without changing behaviour (every snapshot and CLI output stayed byte-identical); the
decision and the alternatives are in [decision 0006](decisions/0006-layers-with-one-way-dependencies.md). Where one type mixed two concerns, the type was split rather than moved whole, and the table says why,
because the reasons still hold for the code as it is:

| Was | Became | Why |
|---|---|---|
| `SqlProject` (read a folder, select, infer) | `SqlScriptFolder` (Sources), `DatabaseModel` (Model), `IrInference` (Inference), and `SqlProject` kept as the composition in the CLI | Reading and inferring are different layers, and no layer below the CLI may know both. Tests and the differential harness still use `SqlProject`, now from `Haruspex.Cli`. |
| `Catalog`, `TableModel`, `ColumnModel` (tables with each column's IR shape and a proved-non-null flag) | `Table`, `Column`, `DeclaredNullability` (Model); `SchemaRules.IsNeverNull` (Semantics); the column's shape in `Typer` (Inference) | The model holds declarations (NULL / NOT NULL, primary key, IDENTITY, computed expression). That NOT NULL, a key column or an identity column is never null is an interpretation of the schema, so it moved to Semantics. Mapping a column to an IR shape is inference. |
| `SqlTypes.ToShape(DataTypeReference)` | `SqlType` (Model: kind, canonical name, arguments as written); `SqlTypes.ToShape(SqlType)` (Inference) | What a type *is* is schema data; what it becomes in the IR is inference's choice. Lower-casing the arguments for the IR's SQL type text stays in Inference. |
| The analyser of a body, now `Typer` and `Walker` (scopes, sources, aliases, column resolution, and shapes) | `Binder`, `QueryScope`, `BoundSource`, `Resolution`, `BindingProblem` (Semantics); the rest in `Typer` and `Walker` (Inference) | Binding needs no IR. A binding problem has its own kind, which Inference maps to the IR's unknown reason. An outer join's optional side is recorded as structure (`BoundSource.Optional`); that it makes a value nullable is decided in Inference. |
| `UnknownShape` built by `SqlProject.Changed` for ALTER/DROP | `Redefinition` (Model), turned into an unknown by Semantics (tables) and Inference (procedures) | The doubt about which definition is in effect is a fact about the source; how it surfaces in the IR is inference's. |
| `Diagnostic`, `SourceLocation` | moved whole to `Haruspex.Diagnostics` | Already one concern. |
| `ObjectName` | moved whole to Model | Names are schema data. |
| `ObjectPattern` | moved whole to the CLI, then, when the config file arrived, to Config | Selection patterns are the config file's concern, not schema data, so they stay out of the publishable Model. |

## The body's own relations

A procedure defines relations of its own: CTEs, temporary tables and table variables; and it reads others whose rows
come from a definition or a call: derived tables, views, table-valued functions, `OPENJSON`. Binding a name to any of
them is Semantics' job, as binding to a table is: the `Binder` binds each query into an immutable `QueryScope` whose
sources read a table or a `RelationSymbol` (a CTE, a derived table with its bound query, a view, a temporary table and the
rest, each with its columns' names), and resolves every column reference to a `Resolution`. The names a body defines are
an argument, a `NameEnvironment`; a CTE hides a table of its name, as in SQL Server. A temporary table's columns at a
statement depend on the path to it, which only the walk knows, so the walk passes them in; binding asks Inference for
nothing. What a relation's columns hold is not binding: Inference attaches values to the bound sources as it reads them
(`ReadScope`), and carries the state of each temporary table from statement to statement (`FlowState`). So Semantics
still never sees a value, and Inference never resolves a name, which `NameResolutionTests` holds by reading Inference's IL: no
method reads a source's alias, a column reference's parts, a table reference's name, a schema object name's parts or the
name of what a body declares, and the few exceptions are listed there with why. What a body declares (its parameters,
variables, table variables and cursors) is read by Semantics too (`BodyDeclarations`), and a variable reference resolves to
a parameter, a local or neither (`VariableResolution`). A temporary table's or table variable's definition is
read into a `Table` by `SyntaxReader.Table` in Syntax, the same reading `CREATE TABLE` gets in Sources, so the
schema rules (`SchemaRules.IsNeverNull`, `ColumnFacts`) apply to it unchanged.

Every join is bound, `APPLY`, `CROSS JOIN` and the outer joins among them: the binder gives an `APPLY`'s right side a scope
of its left input's sources and records which side a source is on (a `RIGHT JOIN`'s left side is optional, as a `LEFT
JOIN`'s right side is), and Inference attaches what each relation holds, a view's read from its definition, as for derived
tables. A permanent table changed by `ALTER TABLE` is reported with the reason `unsupported-source`, because which definition applies
depends on the order the scripts run. The forms that can't make a column nullable, change its type, or add or remove one
are the exceptions: adding a constraint, checking or disabling constraints, rebuilding, and switching triggers or change
tracking (`WeakeningStatements.OnlyTightens`).
