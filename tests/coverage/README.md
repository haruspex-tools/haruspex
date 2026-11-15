# Coverage data

The two lists [the coverage page](../../docs/coverage.md) is written from. `CoveragePageTests`
(`tests/Haruspex.Tests/Coverage`) writes the page from them and from the code, and fails when the committed page differs, so
the page always says what haruspex does. Rewrite it with:

```sh
HARUSPEX_ACCEPT=1 dotnet test --filter CoveragePageTests
```

Then read the diff, and say in the commit why it moved.

## The built-in functions: `sql-server-2025-functions.txt`

Microsoft's T-SQL reference lists SQL Server's built-in functions by category. This file keeps those names, and only the
names: a name is a fact, and nothing of the reference's text is copied. Its head says where and when it was read. It was made
on 15 Nov 2026 from the reference's overview of functions, each category's page and the reference's table of contents,
keeping a name either lists whose own page says it applies to SQL Server 2025 (17.x). So it leaves out what only Azure SQL
Database, Azure Synapse or Fabric has (such as `ANY_VALUE` and the AI functions other than `AI_GENERATE_CHUNKS` and
`AI_GENERATE_EMBEDDINGS`), the ODBC scalar functions, and the system functions the reference lists only by family on a page
of their own. Where a category page and the table of contents disagree, it keeps the names of both. A few choices:

- The reference's rowset functions have no page listing them, so the relational operators' page stands in for them
  (`GENERATE_SERIES`, `OPENJSON`, `OPENROWSET`, `STRING_SPLIT` and the rest).
- `CAST` and `CONVERT` are two names; `IDENTITY` is the function, not the property; `UPDATE` is the trigger function;
  `NEXT VALUE FOR` is kept as written.

The page sets each name against the catalogue of built-in functions (`BuiltInFunctions`). A function of the catalogue the
list doesn't name (`COALESCE` and `NULLIF`, which the reference documents as expressions) is shown in a section of its own.
For any other name, the page shows what haruspex gives for a call of it, found by inferring the call: `FunctionProbes`
writes one, in the form the function needs, such as with `OVER` for a window function or in `FROM` for a rowset.

To refresh the list for a later version, read the same pages again, change the names, and update the head.

## The syntax: `syntax.txt`

An entry for every construct ScriptDom parses: each concrete node type that is a statement, an expression, a predicate, a
table source, a query, a part of a select list, a `FOR` clause, a grouping, an insert source, a merge action or a hint;
each clause of a query or a write; and each member of the enums that say which operator, join, set operator or
assignment a node is. `ScriptDomNodes` reads them from ScriptDom itself, so a construct a new ScriptDom adds fails the test
until it has an entry. The file's head gives the format.

Each entry claims what haruspex does with the construct, and its example shows it:

- `modelled` and `no-effect`: the example, as a procedure's body, gives no unknown.
- `unknown <reason>`: the example gives an unknown with that reason.
- `partly <reason>`: the first example gives no unknown, the second (after `&&`) gives the reason.
- `not-in-a-body`: the parser refuses the example as a procedure's body.
- `not-parsed`: the parser never makes this node from a body; the example, if any, has none.
- `within <node>`: a part of another construct, which that entry describes.
- A bare name: a statement the walker has no case for, which makes the procedure's result sets after it unknown
  (`unsupported-statement`). The test checks against the walker's switch that it has no case for it.

The test checks that every example parses and holds its construct, then infers each one, as a procedure of its own over the
tables in `CoverageProbe`, and checks the unknowns it gives. So the page's claims are what haruspex does, not what was
believed when the entry was written. The examples are invented, over a toy shop. An entry's What field is its own: the test
can't read it, so a reviewer reads it against the example.
