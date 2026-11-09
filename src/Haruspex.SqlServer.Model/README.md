# Haruspex.SqlServer.Model

A SQL Server database's schema as plain .NET data: tables with their columns, declared types, NULL or NOT NULL,
identity and primary keys; stored procedures, with their definitions as
[ScriptDom](https://www.nuget.org/packages/Microsoft.SqlServer.TransactSql.ScriptDom) fragments; and views,
synonyms and functions by name. Names are two-part (`schema.name`) and compared case-insensitively, as SQL Server
compares them in almost every database.

It is meant for tools that need to read a schema without a database and without DacFx: linters, diff tools,
documentation generators, code generators. [Haruspex.SqlServer.Sources](../Haruspex.SqlServer.Sources/README.md)
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
  follows from its declaration, its key and its identity together; what a reference in a procedure body points
  to depends on scopes and the caller's default schema. Those are a semantic layer's questions, not the model's.
- **No guessing an order.** When the source changes an object after defining it (`ALTER TABLE`, `DROP
  PROCEDURE`) and doesn't say which runs last, the model records a `Redefinition` rather than applying one.

It is part of [haruspex](https://github.com/sharpblue/haruspex), and pre-release: its API will change.
