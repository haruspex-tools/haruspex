# Haruspex.SqlServer.Model

A SQL Server database's schema as plain .NET data: tables with their columns, declared types, collations, NULL or
NOT NULL, identity, primary keys and CHECK constraints; alias and CLR types (`CREATE TYPE`); stored procedures and user-defined functions, with their definitions as
[ScriptDom](https://www.nuget.org/packages/Microsoft.SqlServer.TransactSql.ScriptDom) fragments; and views,
synonyms and functions by name. Names are two-part (`schema.name`) and compared case-insensitively, as SQL Server
compares them in almost every database.

`Table.FromDefinition` reads a table definition as written, so a body's own tables (`CREATE TABLE #t`,
`DECLARE @t TABLE`) can be read the same way as the schema's.

It is meant for tools that need to read a schema without a database and without DacFx: linters, diff tools,
documentation generators, code generators. [Haruspex.SqlServer.Sources](https://github.com/sharpblue/haruspex/blob/main/src/Haruspex.SqlServer.Sources/README.md)
fills it from a folder of `.sql` scripts.

```csharp
var model = SqlScriptFolder.Read("path/to/database").Model;
foreach (var table in model.Tables)
    Console.WriteLine($"{table.Name}: {string.Join(", ", table.Columns.Select(c => $"{c.Name} {c.Type}"))}");
```

## What it deliberately doesn't do

- **No building, comparing or deploying.** The model is read-only. It never builds a DACPAC, never writes one,
  and never talks to a server.
- **No knowledge of where the schema came from.** A file path appears only in source locations, so errors can
  point at a line. Reading `.sql` files, and later a DACPAC's `model.xml`, belongs to Sources.
- **No interpretation.** It holds what the source declares and nothing it implies. Whether a column can be null
  follows from its declaration, its key, its identity and its alias type together, and what a CHECK constraint
says about values (a set, a range) is a reading of its expression; what a reference in a procedure body points
  to depends on scopes and the caller's default schema. Those are a semantic layer's questions, not the model's.
- **No guessing an order.** When the source changes an object after defining it (`ALTER TABLE`, `DROP
  PROCEDURE`, `ALTER FUNCTION`) and doesn't say which runs last, the model records a `Redefinition` rather than applying one. A
  CHECK constraint that a later `ALTER TABLE … NOCHECK CONSTRAINT` may disable records where (`DisabledAt`).

It is part of [haruspex](https://github.com/sharpblue/haruspex), and pre-release: its API will change.
