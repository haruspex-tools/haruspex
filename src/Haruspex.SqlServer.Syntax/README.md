# Haruspex.SqlServer.Syntax

Reads what [ScriptDom](https://www.nuget.org/packages/Microsoft.SqlServer.TransactSql.ScriptDom), Microsoft's T-SQL parser,
parsed into a [Haruspex.SqlServer.Model](https://github.com/sharpblue/haruspex/blob/main/src/Haruspex.SqlServer.Model/README.md):
a name, a type, a table definition. It is the one place a declaration is read, wherever it is written. A script's
`CREATE TABLE` is read with it by
[Haruspex.SqlServer.Sources](https://github.com/sharpblue/haruspex/blob/main/src/Haruspex.SqlServer.Sources/README.md),
and a body's own `CREATE TABLE #t` or `DECLARE @t TABLE` by the layers that bind and infer, so the same declaration means
the same thing in both.

## How data flows through it

```mermaid
flowchart LR
    parsed["ScriptDom's syntax tree"] --> reader["SyntaxReader"]
    reader --> name["ObjectName"]
    reader --> type["SqlType"]
    reader --> table["Table"]
    sources["Sources: a script's declarations"] --> reader
    body["Semantics and Inference: a body's declarations"] --> reader
```

## Example

```csharp
var table = SyntaxReader.Table(SyntaxReader.Name(create.SchemaObjectName), create.Definition, "tables/orders.sql", at);
```

## What it guarantees

- **One reading.** A table declared in a body is read by the same code as one declared in the schema, so the schema's rules
  (which columns are never null, what a CHECK constraint allows) apply to both unchanged.
- **Declarations as written.** It reads what the syntax says and nothing it implies.

## What it deliberately doesn't do

- **No files.** It reads syntax trees it is given; which scripts there are, and how they are decoded, is Sources' job.
- **No name resolution.** A name is read as the definition writes it (an unqualified one in `dbo`); what a reference in a
  body means is Semantics' question.

It is part of [haruspex](https://github.com/sharpblue/haruspex), and pre-release: its API will change. Its chapter of
[the guide](https://github.com/sharpblue/haruspex/blob/main/docs/internals/README.md), *The inputs and the model*, is
planned.
