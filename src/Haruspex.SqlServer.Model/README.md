# Haruspex.SqlServer.Model

A SQL Server database's schema as plain .NET data, for tools that need to read a schema without a database and without
DacFx: linters, diff tools, documentation generators, code generators.
[Haruspex.SqlServer.Sources](https://github.com/sharpblue/haruspex/blob/main/src/Haruspex.SqlServer.Sources/README.md)
fills it from a folder of `.sql` scripts or from a DACPAC.

It holds tables with their columns, declared types, collations, NULL or NOT NULL, identity, primary keys, unique keys and
CHECK constraints; alias and CLR types (`CREATE TYPE`); stored procedures, functions of each kind, views and DML triggers,
with their parameters, return types and options as data and their bodies as
[ScriptDom](https://www.nuget.org/packages/Microsoft.SqlServer.TransactSql.ScriptDom) statements and expressions; synonyms
with the name each stands for; and foreign keys whose actions change the referencing table (`CascadingKey`). A column
records whether it is `HIDDEN`, and a table whether it is a graph table, since both change what `SELECT *` returns. Every
object knows where it was read (`SourceSpan`).

## How data flows through it

A `DatabaseModel` is built once and only read after that. Everything in it is a declaration as written.

```mermaid
flowchart LR
    sources["Sources reads scripts"] --> model["DatabaseModel"]
    model --> tables["Table: Column, UniqueKey, CheckConstraint, CascadingKey"]
    model --> code["Module: Procedure, Function, View, Trigger"]
    model --> types["UserType: AliasType, ClrType"]
    model --> named["Synonym: the name it stands for"]
    model -.->|changed after its definition| redefinition["Redefinition"]
```

[Haruspex.SqlServer.Syntax](https://github.com/sharpblue/haruspex/blob/main/src/Haruspex.SqlServer.Syntax/README.md)
reads a table definition as written (`SyntaxReader.Table`), and a body's own tables (`CREATE TABLE #t`,
`DECLARE @t TABLE`, a function's return table) the same way (`SyntaxReader.Declared`). Those are a `DeclaredTable`, named as
the body writes them and in no schema, not a `Table`: the schema's rules read either through `ITable`.

## Example

```csharp
var model = SqlScriptFolder.Read("path/to/database").Model;
foreach (var table in model.Tables)
    Console.WriteLine($"{table.Name}: {string.Join(", ", table.Columns.Select(c => $"{c.Name} {c.Type}"))}");
```

## What it guarantees

- **One of several is a closed set of kinds.** A function is a `ScalarFunction`, an `InlineFunction` or a `TableFunction`, a
  body a `SqlBody`, a `ClrBody`, a `NoBody` or an `EncryptedBody` (text the source doesn't hold), a user type an `AliasType` or a `ClrType`: a reader matches on the kind and gets
  exactly its fields, and no other assembly can add one
  ([decision 0031](https://github.com/sharpblue/haruspex/blob/main/docs/decisions/0031-the-model-is-closed-hierarchies-of-records.md)).
- **ScriptDom only in code.** ScriptDom's types appear only as bodies and expressions; names, types, parameters and options
  are the model's own values.
- **One order.** Every collection of schema objects is in source order: by file, in ordinal order of the relative paths,
  then by where each object starts. The same folder gives the same model however its files are listed.
- **Names are two-part** (`schema.name`, `ObjectName`) and compared case-insensitively, as SQL Server compares them in
  almost every database.
- **Declarations are kept as written**, so a tool reading the model sees what the source says, not an interpretation.
- **What the source records of the database itself is kept, and only that.** A source that records the database's settings
  (a DACPAC does) gives its collation, compatibility level and `SET` defaults (`DatabaseSettings`); a folder of scripts
  doesn't, and `DatabaseModel.Settings` is null. They are recorded, not applied: a column's collation is the one it declares,
  and a caller's options are the caller's. What the source refers to but couldn't read, such as a referenced package that
  wasn't found, is listed (`DatabaseModel.Unread`), so a name missing from the model can say where it may be.
- **An object changed after its definition says so.** When the source changes an object (`ALTER TABLE`, `DROP PROCEDURE`,
  `ALTER FUNCTION`) and doesn't say which runs last, the model records a `Redefinition` rather than applying one. A CHECK
  constraint that a later `ALTER TABLE … NOCHECK CONSTRAINT` may disable records where (`CheckConstraint.DisabledAt`).

## What it deliberately doesn't do

- **No building, comparing or deploying.** The model is read-only. It never builds a DACPAC, never writes one, and never
  talks to a server.
- **No knowledge of where the schema came from.** A file path appears only in source locations, so errors can point at a
  line. Reading `.sql` files and a DACPAC's `model.xml` belongs to Sources.
- **No interpretation.** Whether a column can be null follows from its declaration, its key, its identity and its alias
  type together; what a CHECK constraint says about values (a set, a range) is a reading of its expression; what a
  reference in a body points to depends on scopes and the caller's default schema. Those are
  [Haruspex.SqlServer.Semantics](https://github.com/sharpblue/haruspex/blob/main/src/Haruspex.SqlServer.Semantics/README.md)'s
  questions, not the model's.

It is part of [haruspex](https://github.com/sharpblue/haruspex), and pre-release: its API will change. Its chapter of the guide is
[*The inputs and the model*](https://github.com/sharpblue/haruspex/blob/main/docs/internals/02-the-inputs-and-the-model.md).
