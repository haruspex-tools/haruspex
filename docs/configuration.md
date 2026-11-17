# Configuration: `haruspex.json`

A project's settings live in `haruspex.json`. `haruspex generate` and `haruspex check` look for it in the working directory
and then in each folder above it, or use the files `--config` names, or every one below a folder with `--all`: each file is a
module of its own. Paths in it are relative to the file. It is JSON with comments and trailing commas, as `tsconfig.json` is.
Its JSON Schema is published at [`schemas/haruspex.schema.json`](../schemas/haruspex.schema.json); name it in `$schema` and
editors complete and check the file as you type. `haruspex init` writes a starter file, naming it
([the command line](command-line.md#init)).

**An unknown key is an error**, with its path and line. It is never ignored, because a misspelt setting would
otherwise silently do nothing.

```jsonc
{
  "$schema": "https://github.com/sharpblue/haruspex/raw/main/schemas/haruspex.schema.json",
  "source": "database",                     // the folder of .sql files, a .sqlproj, or a .dacpac
  "references": ["../shared/bin/Release"],  // more folders to find the DACPACs a .dacpac or .sqlproj refers to in
  "collation": "Latin1_General_100_CI_AS",  // the database's default, for a folder of .sql files; see "Value sets"
  "queries": "sql",                         // the application's query files: a folder, files or patterns; see "Query files"
  "out": "src/db",                          // where haruspex.ir.json and what "emit" names go

  "emit": ["zod"],                          // and "json-schema", "mssql"; see "What is written"
  "serverOnly": false,                      // true: the functions import 'server-only'; see "What is written"
  "objects": "strip",                       // or "strict"

  "session": { "ansiWarnings": true },      // what the callers' connections set; see "The callers' session"

  "check": { "unknowns": "report" },        // or "fail"; see "What check fails on"

  "acknowledge": [                          // warnings read and lived with; see "Warnings acknowledged"
    { "code": "HAR1008", "subject": "StampBuildInfo", "why": "it stamps the build's number and adds no scripts" }
  ],

  "naming": {                               // for every schema
    "replace": [{ "pattern": "#$", "with": "Row" }],
    "case": "pascal"                        // or "camel", "preserve"
  },

  "schemas": {                              // keyed by the schema's name as it is
    "sales": { "include": ["get_*", "list_*"], "exclude": ["*_internal"] },
    "audit": { "include": ["*"], "prefix": "Audit" },                          // AuditGetOrder
    "RDF.": { "include": ["GetPropertyList"], "naming": { "case": "preserve" } }
  },

  "procedures": {
    "sales.get_order": {
      "name": "OrderDetail",
      "paths": {
        "lines[]": { "name": "OrderLine" },
        "customer.email": { "nullable": false, "why": "checkout requires an email" },
        "status": { "values": "exact", "why": "the application writes lower case only" },
        "settings": { "type": "OrderSettingsSchema", "import": "@app/orders/settings" }   // see "type" below
      }
    },
    "sales.search_orders": {                // dynamic SQL: see "Declared results" below
      "results": { "rowset": "schemas/order-search-row.schema.json" },
      "why": "the filter is built as dynamic SQL"
    },
    "audit.write_log": { "results": "none", "why": "it only writes the log" }
  }
}
```

The command line overrides the file for one run: the source argument (a folder, a `.sqlproj` or a `.dacpac`) replaces `source`, `--out` replaces `out`, and
`--include schema.pattern` replaces the schemas' includes (each schema's `exclude` still applies, and overrides of
procedures the run leaves out are skipped). Without a file, the source argument, `--include` and `--out` are required.

## Reading a DACPAC: `source` and `references`

`source` may name a `.dacpac` instead of a folder. The package's `model.xml` is then read, without DacFx, with every package
of the same database it refers to, directly or through another, into one model
([decision 0040](decisions/0040-a-dacpac-is-read-with-the-packages-of-its-database.md)); another database's package, or
`master.dacpac`, is read into a model of its own, so that a three-part name in a body reads its tables
([decision 0042](decisions/0042-a-table-of-another-database-is-read-as-its-package-declares-it.md)). A database on another server
isn't read ([HAR1012](diagnostics/HAR1012.md)). The IR records the package and each reference, with the SHA-256 of the file read
for it (`source.package`, `source.references`).

A referenced package is looked for beside the package that refers to it, where a build leaves it; then in each folder
`references` lists, in order, relative to this file; and last at the path the package recorded when it was built. One found
nowhere is a warning ([HAR1003](diagnostics/HAR1003.md)) that lists every place looked, and the run goes on: whatever would
have come from it is unknown, and each such unknown says the package it may be in.

Locations in what is read from a package name the package and the object, with a line in the object's own text:
`sales.dacpac/[sales].[get_order]:3:5` ([decision 0039](decisions/0039-a-location-in-a-dacpac-names-the-package-and-the-object.md)).

## Reading a project file: `source` as a `.sqlproj`

`source` may name a database project's file, `.sqlproj`, which is read as its build would read it, with no MSBuild, no DacFx
and no build: the scripts the build reads, each with the settings it starts with, and the database's settings. Both kinds of
project are read. A `Microsoft.Build.Sql` project's build reads every `.sql` file under its folder (but `bin` and `obj`), less
what `Build Remove`, `None`, the pre- and post-deployment scripts and the files they include with `:r` take out, plus any
`Build` item outside the folder; a `Content` item neither adds a script nor takes one out. A legacy SSDT project's build reads
the `Build` items it lists. A script starts with its item's `AnsiNulls` and `QuotedIdentifier`, or else the project's.

The database's settings are the project's properties (`DefaultCollation`, `CompatibilityMode` or the `DSP`'s version,
`AnsiNulls`, `QuotedIdentifier` and the other options), with the values a build gives one the project doesn't set: the
collation `SQL_Latin1_General_CP1_CI_AS`, every option on but `NUMERIC_ROUNDABORT`. So a project gives the collation a folder
lacks, and the value sets it decides ("The database's collation", below).

Each project of the same database it refers to (a `ProjectReference` naming no other database) is read too, from its own
`.sqlproj` and with its scripts' own settings, as a DACPAC's packages are; one that isn't there is a warning
([HAR1007](diagnostics/HAR1007.md)). What else it refers to is read as a DACPAC's references are, from where its build takes each
([decision 0045](decisions/0045-a-project-s-references-are-read-from-where-its-build-takes-them.md)):

- another database's project (`DatabaseSqlCmdVariable` or `DatabaseVariableLiteralValue`), from its own `.sqlproj`, into a model
  of its own under the name a body writes it by (`[$(VaultDb)]`, `almanac`);
- a DACPAC (`ArtifactReference`), at the path its `Include` gives, of this database or, when it names one, another;
- the DACPAC in a NuGet package (`PackageReference`, in a `Microsoft.Build.Sql` project), such as `master.dacpac` in
  `Microsoft.SqlServer.Dacpacs.Master`: `tools/<name>.dacpac` where a restore of the version the project names put it, in
  `NUGET_PACKAGES`, `.nuget/packages` in your home, or the project's `RestorePackagesPath`. A version that is a range or a
  floating one (`170.*`) isn't read, and is reported, since only a restore against a feed settles it.

A DACPAC not where its build takes it, such as one in a package not restored, or Visual Studio's own `master.dacpac`
(`$(DacPacRootPath)\…\SqlSchemas\master.dacpac`, which only a Visual Studio install has), is looked for by its file name in each
folder `references` lists. A `master.dacpac` found there stands for Visual Studio's: haruspex doesn't check that it is the version
the path names (the `150` in `SqlServer\150`), and the IR records the hash of the file it read. One found nowhere is a warning ([HAR1003](diagnostics/HAR1003.md)) naming where it was looked for. The
IR records each package read, with its SHA-256, in `source.references`, and no `source.package`: the project's own scripts aren't
hashed.

A project file is MSBuild, and haruspex evaluates the part of it a database project uses: properties, conditions on the
configuration, items and their patterns, the nearest `Directory.Build.props`, and the files it imports
([decision 0044](decisions/0044-a-project-file-is-evaluated-in-part-and-the-rest-reported.md)). It is read as `Debug`, as
`dotnet build` builds it, and a project that builds differently as `Release` says so. Whatever isn't evaluated (a property
function, a property defined nowhere, `Choose`, an import it can't find) is never guessed: when it touches a script or a
setting, that script or setting isn't read, and a warning ([HAR1008](diagnostics/HAR1008.md)) says what, where and why.
Locations are relative to the project's folder (`../shared/audit.sql:3:5` for a script beside it); an object read from a DACPAC
is located in it, as when the DACPAC is the source.

## The database's collation: `collation`

A column without `COLLATE` takes the database's default collation. A `.dacpac` records it, and a `.sqlproj` gives it; a folder
of scripts doesn't, so `collation` declares it. A package's or a project's own wins, and one that disagrees with `collation` is
a warning ([HAR6010](diagnostics/HAR6010.md)).

The collation decides how a `CHECK` constraint's set of strings is typed. For a `char` or `varchar` column under a collation
haruspex has proven (`SQL_Latin1_General_CP1_CI_AS`, `Latin1_General_CI_AS`, `Latin1_General_100_CI_AS`,
`Latin1_General_100_CS_AS`, `Latin1_General_100_CI_AI`), a set of plain strings (ASCII letters and digits, space, `_`, `-`,
`.`) is an enum: a value is first mapped to the string it equals under the collation, so `'PAID'` and `'paid '` both arrive as
`"paid"` ([decision 0043](decisions/0043-a-value-set-is-canonical-by-a-fold-observed-from-the-server.md)). Any other set, and
every set of an `nchar` or `nvarchar` column, is a comment saying the values match as the database compares strings; an
override can still declare it exact (`"values": "exact"`).

## Which procedures are typed: `schemas`

Settings are per schema. Each key of `schemas` is a schema's name exactly as in the database: no brackets, dots
allowed (`"RDF."`). Its block holds:

- `include` and `exclude`: patterns matching the schema's procedures by their **own names**, ignoring case. `*` matches
  any characters, `?` one; brackets make what they hold literal (`[a*b]`). `exclude` wins over `include`.
- `prefix`: put in front of the names of the schema's objects (see Naming). Empty by default.
- `naming`: the schema's own naming rules, merged over the top-level ones: its `case` replaces the top-level case, and
  its `replace` rules run after the top-level ones.

**No procedure is selected unless its schema has a block with `include`**: typing every procedure is something you ask for
(`"include": ["*"]` per schema), never the default. Whatever matches nothing is an error: a schema key that names no
schema of the project, an include that selects nothing in its schema (a typo would otherwise drop an interface without
anyone noticing), an exclude that removes nothing its include selected, an exclude in a block without an include, and a
block whose exclude removes everything its include selects. A one-off `--include` whose every match a schema's
exclude leaves out is an error too: a run that would type nothing fails rather than writing an empty module.

A block is an object so that settings can be added to it later (one output module per schema is planned) without
breaking a file written today.

## Query files: `queries`

An application that keeps queries in `.sql` files of its own, rather than in procedures, names them in `queries`: a folder (every
`.sql` file below it), a file, or a pattern, relative to the config file (`*` is any characters but `/`, `**` any folders, `?` one
character), or an array of them. Each file is typed with the procedures, against the same project, and is one object of the
module, named from its file by the naming rules: `get_order_card.sql` is named GetOrderCard, and under `"case": "camel"`
`getOrderCard.sql` keeps its name, getOrderCard. Its id in the IR is its path relative to the config file's folder.

```jsonc
{
  "source": "database",
  "out": "src/db",
  "queries": ["sql", "reports/**/*.sql"],
  "schemas": { "sales": { "include": ["get_*"] } }   // optional: query files alone are a selection too
}
```

A query file is one batch, run through `sp_executesql` as it is written, with no `GO`
([HAR1014](diagnostics/HAR1014.md)). Its parameters are the variables it uses without declaring. Each is typed from its uses,
as SQL Server's own `sp_describe_undeclared_parameters` types it: compared with a column, `where o.order_id = @order_id`, it takes
the column's type, `int`; passed to a procedure, the procedure's parameter's type; and so on
([the rules](../src/Haruspex.SqlServer.Inference/README.md#query-files)). Where nothing in the file decides a parameter's type,
or two uses decide differently, it is unknown ([HAR2024](diagnostics/HAR2024.md)), and a comment in the file declares it, one
to a line, with the type written as `DECLARE` writes it:

```sql
-- @param @label nvarchar(40)
-- @param @since datetime2(0)
SELECT @label AS label, o.order_id
FROM sales.orders AS o
WHERE o.placed >= @since;
```

A declaration that can't be taken, one that names a variable the file doesn't use, say, is an error
([HAR1015](diagnostics/HAR1015.md)). [Decision 0059](decisions/0059-a-query-file-s-parameters-are-typed-from-their-uses.md) says
why parameters are typed this way.

**A query file belongs to the module whose config file's folder holds it.** An entry can't reach above the config file
([HAR3022](diagnostics/HAR3022.md)), and a folder below with a `haruspex.json` of its own is that module's, so its files are
passed over; an entry that names no file of this module is an error ([HAR4007](diagnostics/HAR4007.md)), as an include that
selects nothing is. A query file's name and a procedure's share the module's names: one that clashes is an error naming both
([HAR4303](diagnostics/HAR4303.md)), and neither is renamed for the other.

## Overrides

An override corrects or completes what was inferred, for one procedure. Overrides live in `procedures`, keyed by the
procedure's two-part name (`sales.get_order`, with T-SQL brackets around a part that holds a dot), which must be selected;
only `results`, below, may be given for any procedure. Overrides are applied to the IR after inference, never inside it, and every one stays **checked at
runtime**: the generated validator still checks every value, so a wrong override fails loudly on real data; it is never
a silent cast. The IR lists every claim an override made under `overrides`, with its reason.

- `name`: the procedure's generated name.
- `paths`: places in its results, in the IR's path syntax, relative to the result: `[]` for a list's rows, `.key` (the
  first dot may be left out) or `["a key"]`, `{}` for a record's values, `[0]` for a tuple's position, `<json>` for
  what JSON text holds. A procedure with several result sets names one: `results[1][].id`. Each path takes:
  - `name`: the name of the object type there. Types inside it build their names on it.
  - `nullable: false` with a required `why`: the value is never null, which the database doesn't prove. A key `FOR
    JSON` leaves out when null then becomes required.
  - `type` and `import`: the value is the application's own schema, `type` exported by the module `import`, with
    `[]` for each level of array (`"TagSchema[]"`). Any unknowns there are then declared, not reported. A relative
    `import` (`./`, `../`) is relative to this file, and is rewritten relative to `out`; a package or alias is kept as
    written. **The imported schema receives the value as it arrives.** At JSON text (a `json` or JSON-holding column
    returned in a rowset, a `FOR JSON` subquery `WITHOUT_ARRAY_WRAPPER`) that is the string, not what it parses to, so
    the schema must parse it itself, as `z.string().transform((t) => JSON.parse(t)).pipe(OrderSettings)` does; a plain
    `z.object(…)` there would reject every row. Inside live JSON (`FOR JSON`, `json_query`) it receives the parsed
    value.
  - `values: "exact"` with a required `why`: a value set that the database compares under its collation (where `'Open'`
    can pass a check for `'open'`) holds exactly its items, giving a `z.enum`. It needs a value set the IR already has,
    from a CHECK constraint on a column the value passes through unchanged.

**An override whose path doesn't exist is an error**, naming the keys that do. **One the inference already agrees
with is a warning**, so the file shrinks as the SQL improves. Generation goes ahead after a warning.

## Declared results: `results`

Some procedures' result sets can't be known from the project: dynamic SQL (`EXEC (@sql)`, `sp_executesql`), a procedure named
in a variable, a system procedure, a CLR procedure, one in another database or not in the project, a call without a schema
that names a procedure in more than one (which one runs depends on the caller's default schema), and a module created
`WITH ENCRYPTION`, whose text the project doesn't hold. Their results are unknown, and so are those of
every procedure that calls them, with a reason naming the statement. `generate` warns about each selected procedure whose
results are unknown for one of these reasons, naming the statement and what to declare; it still writes its output, and
exits as before (the `check` command will fail it).

`results` declares them, with a required `why`:

```jsonc
"procedures": {
  "audit.write_log": { "results": "none", "why": "it only writes the log" },
  "sales.search_orders": {
    "results": [
      { "rowset": { "type": "object", "properties": {
          "order_id": { "type": "integer", "format": "int32" },
          "note": { "type": ["string", "null"] } } } },
      { "json": "schemas/search-totals.schema.json", "mayBeAbsent": true }
    ],
    "why": "the filter is built as dynamic SQL"
  }
}
```

- `"none"`, one result, or an array of them in the order the procedure returns them. A result is `{ "rowset": schema }`, the
  JSON Schema of **one row** (an object whose `properties` are its columns, each a scalar and always there), or
  `{ "json": schema }`, the JSON Schema of the document `FOR JSON` writes, with `"mayBeAbsent": true` when it may match no
  rows (FOR JSON then returns no row at all); the generated schema then accepts `null`, and its summary names the declaration as
  the reason ("null when absent, as the config file declares"). A schema is written inline, or as the path of a file holding it, relative to
  this file; a problem in a file is reported in that file, with its line.
- **The schema keywords read:** `type` (one type, with `"null"` when it may be null), `properties` and `required`, `items`,
  `minItems` and `maxItems`, `additionalProperties` (alone, for an object whose keys are data), `$ref` to the schema's own
  `$defs` or `definitions` (not recursive), and `format`: `date`, `time`, `date-time` (with an offset), `date-time-local`
  (without one, as SQL Server writes `datetime2`), `uuid`, and `int32` or `int64` on an `integer`. An `integer` without a
  format is checked as a number. Annotations (`title`, `description`, `$schema`, …) are ignored. **Any other keyword is a
  warning** and is not checked, so the generated type is wider than the schema, never narrower. A key of a JSON object that
  is not `required` may be left out but is never null, as `FOR JSON` writes it; one that may be both is an error.
- **Any procedure** can be declared: one that isn't selected, or isn't in the project. A call of it contributes the declared
  result sets to its caller, and the caller's IR lists that claim (an override with `via`). What it does to temporary tables
  is not known either way, so after a call of it they are unknown. A declaration that no selected procedure uses is a
  warning.
- **A declaration of results haruspex can infer** is an error when it disagrees, naming where, and a warning when it agrees,
  so it can be removed. They are compared as far as a declaration can say: how many result sets, each one's encoding and
  whether it may be absent, and in every value its kind, its keys, and whether it may be null or left out. To correct part of
  inferred results, use `paths` instead; `paths` and `results` can't be used together.
- **`INSERT … EXEC` staging.** A call whose results go into a temporary table (`INSERT INTO #t EXEC …`) and isn't followed
  (dynamic SQL, or a procedure running it) leaves every temporary table unknown, because it could change any of them. A
  declaration of the procedure called doesn't change that: it says what the procedure returns, not what it does to
  temporary tables. `generate` warns of each such call, naming it; to type the values read back from the table, override
  their paths under `paths` with `type` and `import`.
- **Declared results are checked at runtime**, like everything else: the generated validator rejects output that doesn't
  fit the declaration, so a wrong one fails loudly on real data.

## What is written: `emit`

`generate` writes the IR, `haruspex.ir.json`, to the output folder, and beside it what `emit` names, each made from the IR
alone:

- `"zod"`, the default: `haruspex.zod.ts`, a TypeScript module of Zod schemas and their types
  ([the Zod emitter](../src/Haruspex.Emit.Zod/README.md)).
- `"json-schema"`: `haruspex.jsonschema.json`, one JSON Schema document (draft 2020-12), for any language with a JSON Schema
  validator. Each procedure's results are the definition under its name in `$defs`, `#/$defs/GetOrder`, the same name the Zod
  module exports; every value is described as SQL Server's `FOR JSON` writes it, a rowset's rows as an array of objects as
  `FOR JSON PATH, INCLUDE_NULL_VALUES` writes them
  ([the JSON Schema emitter](../src/Haruspex.Emit.JsonSchema/README.md),
  [decision 0051](decisions/0051-one-json-schema-document-per-module-every-value-as-json.md)).

- `"mssql"`: `haruspex.mssql.ts`, the database module's functions, one for each procedure and query file, which check its
  parameters, run it through the mssql driver and check what comes back ([the mssql emitter](../src/Haruspex.Emit.Mssql/README.md)). They import
  the schemas and types from `haruspex.zod.ts`, so `"mssql"` needs `"zod"` beside it.

```jsonc
"emit": ["zod", "json-schema"]   // both, each from the same IR
```

```jsonc
"emit": ["zod", "mssql"]   // the schemas, and the functions that use them
```

**The functions belong on the server.** They take a connection, and a connection holds the database's credentials; and the
database module is two files so that the schemas and types, which a browser may use, stay apart from the functions, which it
must not. In a Next.js application they are for route handlers, server actions and server components. `"serverOnly": true`
starts `haruspex.mssql.ts` with `import 'server-only'`, so that a build fails where client code imports it (install the
`server-only` package); it needs `"mssql"` in `emit`, and is off by default, since haruspex assumes no framework.

`emit` lists at least one, each once. `check` compares each file it names with the one in the output folder, and fails when one is
missing. A file of an emitter taken off the list is left where it is, not deleted: remove it yourself.

## Results policy: `objects`

- `"strip"`, the default: `z.object`, and JSON Schema objects that allow other keys. Types are still exact, but keys a type
  doesn't list are removed when parsing, or allowed when validating, so a column the database gains before the application
  redeploys doesn't break the running application.
- `"strict"`: `z.strictObject`, and JSON Schema objects that allow no other keys, for projects that deploy the application first or together
  with the database, and want an unexpected key to fail loudly.

This is the one deliberate exception to haruspex's rule that the generated code claims everything the engine guarantees
([decision 0005](decisions/0005-claim-everything-the-engine-guarantees.md)): `FOR JSON` produces exactly the known
keys, so strict is the accurate schema, and strip is the default for deployment safety. It applies to results only.

## What check fails on: `check`

`haruspex check` fails when what the module claims would change, when the output folder holds nothing to compare with, and when a selected
procedure's result sets are unknown and not declared (the CLI's README says each). An unknown value is a note, as in `generate`,
and doesn't fail it, since the module still checks it at runtime as a value of any shape. For a module that must be typed
throughout:

```jsonc
"check": { "unknowns": "fail" }   // any unknown fails the check (HAR6017); "report", the default, notes each
```

`unknowns` is `"report"`, the default, or `"fail"`.

## Warnings acknowledged: `acknowledge`

Some warnings say something about the source that a team has read and decided to live with: a build target that only stamps
a version, a reference to a package a build machine doesn't have. Said on every run, they teach the team to skip warnings,
and the next one, which matters, is skipped with them. `acknowledge` names each such warning by its `code` and its `subject`
together, never by its code alone, with `why`:

```jsonc
"acknowledge": [
  { "code": "HAR1008", "subject": "RunTestsBeforeDeploy", "why": "it runs the tests and adds no scripts" },
  { "code": "HAR1003", "subject": "master.dacpac", "why": "no procedure reads a system view" }
]
```

An acknowledged warning is counted rather than said (`haruspex: nothing changed since the last run: 4 procedures, no
unknowns; 2 warnings acknowledged (--verbose lists them).`); `--verbose` lists each, with its `why`, and `--format json` and
`sarif` hold them apart, SARIF as suppressed results. Every other warning of the same code is still said. The subject is
the name the warning gives:

| Code | Subject |
|---|---|
| [HAR1003](diagnostics/HAR1003.md), a referenced DACPAC isn't found | its file name, such as `master.dacpac` |
| [HAR1007](diagnostics/HAR1007.md), a referenced project isn't found | its file name, such as `Shared.sqlproj` |
| [HAR1008](diagnostics/HAR1008.md), part of a project file isn't read | the name of the build target that isn't read; the other parts can't be acknowledged |
| [HAR2101](diagnostics/HAR2101.md), a write to an object the project doesn't define | the object, as the warning names it |

No other code can be acknowledged: an error must be fixed, and a warning about the config file is fixed in it (HAR6024). An
entry that matches no warning in a run is said, as a warning (HAR6025), so that one left behind after its cause is gone doesn't
hide the next warning of that subject; a run whose `--include` narrows the selection doesn't say it, since the warning may be
about a procedure the run left out.

## The callers' session: `session`

Whether some values can be null depends on `SET` options of the session that calls the procedure, not on the procedure:
with `ANSI_WARNINGS` and `ARITHABORT` both off, an overflow, a division by zero or a `COUNT` past `int` is NULL; with either
on, it is an error that fails the call. With `NUMERIC_ROUNDABORT` on, a decimal rounded to fewer places is NULL in the same
session, and an error in any other. The caller is out of sight, so by default haruspex assumes nothing, and each such value
is nullable (`arithmetic-overflow`, `count-overflow`, `domain-error`, `precision-loss` in the IR). Declare what your callers
set and those reasons go:

```jsonc
"session": {
  "ansiWarnings": true,             // ANSI_WARNINGS
  "arithAbort": false,              // ARITHABORT
  "concatNullYieldsNull": true,     // CONCAT_NULL_YIELDS_NULL
  "numericRoundAbort": false        // NUMERIC_ROUNDABORT
}
```

- **`ansiWarnings`** or **`arithAbort`** `true`: an overflow, a division by zero, a `COUNT` past `int`, a value outside a
  math function's domain and a loss of precision are errors, so none makes a value nullable. Either one is enough, but a
  loss of precision under `TRY_CAST` or `TRY_CONVERT` is NULL all the same: only `numericRoundAbort` `false` removes that.
- **`numericRoundAbort`** `false`: a decimal rounded to fewer places (a narrowing `CAST`, a division or a product whose scale
  is cut, `AVG` of a decimal, an assignment to a variable of fewer places) is rounded silently, so it never makes a value
  nullable. `true` changes nothing: that is what haruspex assumes when it isn't known.
- **`concatNullYieldsNull`** `false`: string `+` with one side NULL gives the other side, so it is null only when both sides
  may be. `true` changes nothing: that is what haruspex assumes when it isn't known.
- **An option left out is not known**, and haruspex claims only what holds whatever it is. With no `session` block every
  value a caller's options could make NULL is nullable, a loss of precision included since IR 0.18, which made such values
  nullable that earlier versions claimed never null.

Declare what the **callers' connections** set, not the database's defaults: those apply only to a connection that sets
nothing, and current drivers set these options at login. tedious (the `mssql` package's driver) sets the first three on and
`NUMERIC_ROUNDABORT` off; Microsoft.Data.SqlClient sets `ANSI_WARNINGS` and `CONCAT_NULL_YIELDS_NULL` on and `ARITHABORT` and
`NUMERIC_ROUNDABORT` off; the ODBC and OLE DB drivers set `ANSI_WARNINGS` on. [Decision 0038](decisions/0038-the-callers-session-is-declared.md) lists the sources, and
why each option is or isn't here. An application can `SET` its own after connecting, so when in doubt, ask the session:
`SELECT SESSIONPROPERTY('ANSI_WARNINGS')`.

A declaration is a claim, like `"nullable": false`: if a caller sets the options otherwise, a value the schema says is never
null can be NULL. The IR records it (`source.session`) and the generated module's header says it, so a generated file says what it
assumed. A procedure that sets one of these options otherwise in its body overrides the declaration until it
returns; haruspex then leaves its results unknown. Other options that change no claim haruspex makes (`DATEFIRST`,
`LANGUAGE`, `DATEFORMAT`) are not settings; writing one is an unknown setting.

## Naming

Generated names come from each object's own name, **never its schema**, by declarative rules applied in this order:

1. `replace`: rules of the form `{ "pattern": "…", "with": "…" }`, run over the name in order, each replacing every match
   of the regular expression `pattern` with `with` (`$1` names a group, and `""` removes the match); the top-level rules
   first, then the schema's own. `#$` is the end of the name. Expressions run without backtracking, so
   backreferences and lookarounds are not available.
2. `prefix`: the schema's prefix, if any, goes in front as words of its own: `"prefix": "Audit"` makes
   `audit.get_order` `AuditGetOrder`.
3. `case` (the schema's, or else the top level's): `pascal` (the default) breaks words at every character that isn't a
   letter or digit (`$`, `#`, `_`), so `stock$level$history` gives `StockLevelHistory`. `camel` gives
   `stockLevelHistory`. `preserve` keeps the prefix and the name as they are, `$` included; anything that is still
   not a valid identifier is an error naming the object.

Nested types build on their procedure's name: `GetOrderRow` for its rows, `GetOrderLine` for the items of `lines`,
`GetOrderResult2Row` for a second result set's rows, and `PartTreeNode` for the recursive type of a function
`bom.part_tree` (named by its own schema's settings).

**Names don't shift.** When two objects' names come out the same (two schemas with a `get_order` each, say), it is an
error naming both, which asks for a `prefix` on one of the schemas or a `name` override; haruspex never adds a number
between objects, because that renumbered names whenever an object was added and broke code that used them. Inside one
procedure two types can still meet (a nested key `row` beside the row type, or the variants of a union, which share a
path); there a number is added in the order the procedure's results are written (`GetOrderRow2`), because it depends
on nothing but that procedure.

## Errors

Every error and warning is written to standard error as a compiler writes one, so editors and CI can jump to it, with its
code, whose page in [the list of diagnostics](diagnostics/README.md) says what it means and what to do. (Notes, one for each
unknown, are counted rather than listed unless `--verbose` asks; [the command line](command-line.md) says what each run prints,
and how to have the same as JSON, SARIF or GitHub annotations.)

```text
haruspex.json:14:9: error HAR4104: procedures["sales.get_order"].paths["lines[].skew"]: the path results[0].lines[].skew does not exist: the object at results[0].lines[] has no key "skew". Its keys are "sku", "quantity".
haruspex.json:22:7: warning HAR4110: procedures["sales.get_order"].paths.id: "nullable": false agrees with the inference, which already proves the value at results[0].id is never null. Remove it.
haruspex.json: error HAR4303: "GetOrder" would name both the procedure audit.get_order and the procedure sales.get_order. Generated names must not clash: give one of their schemas a "prefix" in "schemas", …
```

Errors in the config file exit with code 2, as command line mistakes do, and nothing is written; [the command
line](../src/Haruspex.Cli/README.md#what-it-guarantees) lists every exit code.
