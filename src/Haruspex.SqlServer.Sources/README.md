# Haruspex.SqlServer.Sources

Reads a SQL Server database project into a [Haruspex.SqlServer.Model](../Haruspex.SqlServer.Model/README.md):
today a folder of `.sql` scripts, such as an SSDT or `Microsoft.Build.Sql` project's; later a DACPAC's
`model.xml` as well. It parses with [ScriptDom](https://www.nuget.org/packages/Microsoft.SqlServer.TransactSql.ScriptDom),
Microsoft's own T-SQL parser (MIT), and needs neither DacFx nor a database.

```csharp
var read = SqlScriptFolder.Read("path/to/database");
foreach (var diagnostic in read.Diagnostics)
    Console.Error.WriteLine(diagnostic);          // file:line:column: error: message
var orders = read.Model.FindTable(new ObjectName("sales", "orders"));
```

Files are read in order of their relative path, so the result doesn't depend on the file system. Each file is
UTF-8 unless a byte order mark says otherwise (SSMS writes UTF-16), and is parsed with the SQL Server 2025
grammar. Parse errors and objects defined twice are reported as diagnostics, with their file, line and column.

## What it deliberately doesn't do

- **No building or deploying.** It never builds, writes or publishes a DACPAC, and never connects to a server.
- **No DacFx.** DacFx's licence asks anyone who redistributes it to indemnify Microsoft, which no free tool
  should have to take on; ScriptDom is enough to read a project.
- **No running scripts.** A folder of scripts doesn't say the order they run in, so a statement that changes an
  object after its definition (`ALTER TABLE … ALTER COLUMN`, `DROP TABLE`, `ALTER PROCEDURE`, `DROP
  PROCEDURE`) is recorded as a `Redefinition`, not applied. Changes made only through code that would have to
  run, such as `EXEC sp_rename` or dynamic SQL, are not seen.
- **No name resolution or typing.** What a reference in a body points to, and what type an expression has, are
  questions for a semantic layer over the model.

It is part of [haruspex](https://github.com/sharpblue/haruspex), and pre-release: its API will change.
