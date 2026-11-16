# haruspex

A tool that carries a SQL Server database's types into TypeScript by static analysis. It reads a database project's source
and infers what each stored procedure takes and returns, `FOR JSON` documents included. From that it writes
[Zod](https://zod.dev) schemas and types, and a function for each procedure that checks what goes in before calling it and
what comes back before returning it. It is pre-release: see [Status and roadmap](#status-and-roadmap).

```ts
import { getOrder } from './db/haruspex.mssql'

const { result: order } = await getOrder(pool, { OrderID: 42 })   // the order, typed and checked, or null
```

## The problem

Many applications reach their database through stored procedures, and nothing at that boundary is typed. A procedure's
result reaches the application as rows the driver can't describe, or as one string of JSON: SQL Server reports a `FOR JSON`
query as a single `nvarchar(max)` column, however deep the document inside it. So developers write the types by hand. Nothing
checks them against the SQL, and they drift as the procedures change, until a field that "can't be null" is null in
production.

The way in is no safer. A parameter is whatever the application hands the driver, and SQL Server doesn't refuse a value its
declared type can't hold: a string longer than `nvarchar(50)` arrives cut to 50 characters, and a decimal with more places
than its scale arrives rounded, without an error.

The tool works from the source alone (a folder of `.sql` files, an SSDT `.sqlproj` or a DACPAC). It never connects to a
database, so it needs no credentials and no data. And it follows one rule: **never claim more than the engine guarantees.**

## What an application gets

With `"emit": ["zod", "mssql"]` in its `haruspex.json`, `haruspex generate` writes the database module: `haruspex.zod.ts`, a
schema and a type for each procedure's parameters and results, and `haruspex.mssql.ts`, a function for each procedure, which
calls it through the [mssql](https://www.npmjs.com/package/mssql) driver. A function:

- **takes a connection, and the procedure's parameters by their declared names.** The connection is anything with
  `request()`: a pool, a transaction or a test double. The application keeps it, with its pooling, retries and transactions.
- **checks the parameters before anything is sent**, against a strict schema of each one's declared type. A misspelt name, a
  string too long, a decimal with too many places or an integer out of range fails in the application, naming the procedure
  and the parameter, rather than reaching a database that would change it without a word
  ([decision 0054](docs/decisions/0054-a-parameter-is-checked-against-its-declared-type.md)). A date or time is an ISO 8601
  string of the form its type holds, never a `Date`, whose meaning would depend on the time zone
  ([decision 0055](docs/decisions/0055-dates-and-times-are-sent-as-iso-8601-strings.md)).
- **checks what comes back** with the schemas of its results and its `OUTPUT` parameters, and returns them typed, as
  `{ result, output, returnValue }`.

A call fails in one of three ways, and a caller tells them apart by the error's `name`, without importing anything
([decision 0056](docs/decisions/0056-a-function-throws-three-kinds-of-failure.md)):

| What went wrong | What is thrown | What it means |
|---|---|---|
| The parameters | a HaruspexParameterError, before the connection is touched | The calling code is wrong; nothing was sent. |
| The database raised an error | mssql's own `RequestError`, unchanged | Handled as the application handles the driver's errors today. |
| The result broke its schema | a HaruspexResultError, naming the procedure and where | The database and the application are deployed out of step, which no retry fixes. |

**The functions belong on the server.** They take a connection, which holds the database's credentials: in a Next.js
application, they are for route handlers, server actions and server components. The schemas and types are a file of their
own, which browser code may import, and `"serverOnly": true` makes a Next.js build fail where browser code imports the
functions. [The database module](docs/database-module.md) has the rest: what a function returns for each kind of procedure,
how parameters are checked, and how to handle each failure.

## Two examples on Microsoft's samples

Each example below is written for this page over the real tables of one of Microsoft's sample databases, which the
repository keeps in [`tests/corpus`](tests/corpus/README.md). A test infers each over the whole sample and fails if the code
shown here differs from what haruspex writes, and the differential tests run each on SQL Server, through the generated
function, and check every value that comes back against the claims.

### An order with its lines, from WideWorldImporters

```sql haruspex-example WideWorldImporters
CREATE PROCEDURE Website.GetOrder
    @OrderID int
AS
SET NOCOUNT ON;

SELECT o.OrderID AS id,
       o.OrderDate AS orderDate,
       o.ExpectedDeliveryDate AS expectedDelivery,
       o.CustomerPurchaseOrderNumber AS purchaseOrderNumber,
       sp.FullName AS [salesperson.name],
       sp.EmailAddress AS [salesperson.email],
       (SELECT ol.Description AS description,
               ol.Quantity AS quantity,
               ol.UnitPrice AS unitPrice,
               si.StockItemName AS [stockItem.name],
               si.Brand AS [stockItem.brand],
               si.IsChillerStock AS [stockItem.chilled]
        FROM Sales.OrderLines AS ol
        JOIN Warehouse.StockItems AS si ON si.StockItemID = ol.StockItemID
        WHERE ol.OrderID = o.OrderID
        ORDER BY ol.OrderLineID
        FOR JSON PATH) AS lines
FROM Sales.Orders AS o
JOIN Application.People AS sp ON sp.PersonID = o.SalespersonPersonID
WHERE o.OrderID = @OrderID
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
```

What haruspex writes for it, the module's header left out:

```ts haruspex-zod
export const GetOrderSalespersonSchema = z.object({
  name: z.string().max(50),
  email: z.string().max(256).optional(),
})
export type GetOrderSalesperson = z.infer<typeof GetOrderSalespersonSchema>

export const GetOrderLineStockItemSchema = z.object({
  name: z.string().max(100),
  brand: z.string().max(50).optional(),
  chilled: z.boolean(),
})
export type GetOrderLineStockItem = z.infer<typeof GetOrderLineStockItemSchema>

export const GetOrderLineSchema = z.object({
  description: z.string().max(100),
  quantity: z.int32(),
  /** decimal(18,2) runs from -9999999999999999.99 to 9999999999999999.99, which JavaScript reads as the nearest doubles. */
  unitPrice: z.number().min(-10000000000000000).max(10000000000000000).multipleOf(1e-2).optional(),
  stockItem: GetOrderLineStockItemSchema,
})
export type GetOrderLine = z.infer<typeof GetOrderLineSchema>

export const GetOrderRowSchema = z.object({
  id: z.int32(),
  orderDate: z.iso.date(),
  expectedDelivery: z.iso.date(),
  purchaseOrderNumber: z.string().max(20).optional(),
  salesperson: GetOrderSalespersonSchema,
  lines: z.array(GetOrderLineSchema).min(1).optional(),
})
export type GetOrderRow = z.infer<typeof GetOrderRowSchema>

/** Website.GetOrder: its parameters, checked before anything is sent. */
export const GetOrderParamsSchema = z.strictObject({
  OrderID: z.int32().nullable(),
})
export type GetOrderParams = z.infer<typeof GetOrderParamsSchema>

/** Website.GetOrder: returns JSON, null when no rows match. */
export const GetOrderSchema = GetOrderRowSchema.nullable()
export type GetOrder = z.infer<typeof GetOrderSchema>
```

Each line has its reason in the SQL. `email`, `brand` and `purchaseOrderNumber` are optional rather than nullable, because
their columns allow NULL and `FOR JSON` leaves a null key out rather than writing `null`. `lines` has at least one item when
it is there, and is absent for an order with none, because a `FOR JSON` subquery over no rows is NULL, not `[]`. The
salesperson is always there: the join is on a foreign key that is never NULL. The whole result is one object or null, because
the filter is on the primary key and `WITHOUT_ARRAY_WRAPPER` writes a single object.

And the function it writes, after the error classes and helpers every module of functions begins with:

```ts haruspex-mssql
/** Calls Website.GetOrder. */
export async function getOrder(db: Queryable, params: types.GetOrderParams): Promise<{ result: types.GetOrder; returnValue: number }> {
  const p = await checked(GetOrderParamsSchema, params, (issues) => new ParameterError('Website.GetOrder', issues))
  const request = db.request()
  request.input('OrderID', sql.Int, p.OrderID)
  const response = await request.execute('Website.GetOrder')
  const sets = response.recordsets as unknown as readonly unknown[]
  const result = await checked(GetOrderSchema, readJson(sets[0]), (issues) => new ResultError('Website.GetOrder', 'the results', issues))
  return { result, returnValue: response.returnValue }
}
```

It checks the parameters before it asks for a request, binds `OrderID` as the `int` it is declared, and reads the `FOR JSON`
result as the driver hands it over, whether the pool parses JSON or not, before checking it. `OrderID` accepts `null`,
because a procedure can't declare a parameter `NOT NULL`.

### A bill of materials as a tree, from AdventureWorks

A function that calls itself builds a tree of any depth, and haruspex types it as a recursive schema:

```sql haruspex-example AdventureWorks
CREATE FUNCTION Production.GetComponents(@ProductAssemblyID int)
RETURNS nvarchar(max)
AS
BEGIN
    RETURN (
        SELECT b.PerAssemblyQty AS quantity,
               b.UnitMeasureCode AS unit,
               p.ProductID AS [product.id],
               p.Name AS [product.name],
               p.ProductNumber AS [product.number],
               p.StandardCost AS [product.standardCost],
               JSON_QUERY(Production.GetComponents(p.ProductID)) AS components
        FROM Production.BillOfMaterials AS b
        JOIN Production.Product AS p ON p.ProductID = b.ComponentID
        WHERE b.ProductAssemblyID = @ProductAssemblyID
          AND b.EndDate IS NULL
        ORDER BY p.ProductID
        FOR JSON PATH);
END
GO
CREATE PROCEDURE Production.GetProductTree
    @ProductID int
AS
SET NOCOUNT ON;

SELECT p.ProductID AS id,
       p.Name AS name,
       p.ProductNumber AS number,
       JSON_QUERY(Production.GetComponents(p.ProductID)) AS components
FROM Production.Product AS p
WHERE p.ProductID = @ProductID
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
```

```ts haruspex-zod
export const GetComponentsNodeProductSchema = z.object({
  id: z.int32(),
  name: z.string().max(50),
  number: z.string().max(25),
  /** money runs from 0 to 922337203685477.5807, which JavaScript reads as the nearest doubles. */
  standardCost: z.number().min(0).max(922337203685477.6).multipleOf(1e-4),
})
export type GetComponentsNodeProduct = z.infer<typeof GetComponentsNodeProductSchema>

export const GetComponentsNodeSchema = z.object({
  quantity: z.number().min(1).max(999999.99).multipleOf(1e-2),
  unit: z.string().length(3),
  product: GetComponentsNodeProductSchema,
  get components() { return z.array(GetComponentsNodeSchema).min(1).optional() },
})
export type GetComponentsNode = z.infer<typeof GetComponentsNodeSchema>

export const GetProductTreeRowSchema = z.object({
  id: z.int32(),
  name: z.string().max(50),
  number: z.string().max(25),
  components: z.array(GetComponentsNodeSchema).min(1).optional(),
})
export type GetProductTreeRow = z.infer<typeof GetProductTreeRowSchema>

/** Production.GetProductTree: its parameters, checked before anything is sent. */
export const GetProductTreeParamsSchema = z.strictObject({
  ProductID: z.int32().nullable(),
})
export type GetProductTreeParams = z.infer<typeof GetProductTreeParamsSchema>

/** Production.GetProductTree: returns JSON, null when no rows match. */
export const GetProductTreeSchema = GetProductTreeRowSchema.nullable()
export type GetProductTree = z.infer<typeof GetProductTreeSchema>
```

The schema knows what the database declares: `quantity` is at least 1 and `standardCost` at least 0 from the sample's
`CHECK` constraints, `name` is at most 50 characters from its alias type `Name`, and `unit` is exactly three characters
because `nchar(3)` arrives padded. A part with no components has no `components` key.

## Other ways to use it

**The types and parsers alone.** Without `"mssql"` in `emit`, `generate` writes the schemas and types only, for a team that
calls the driver itself, or through code of its own. It parses each result with its schema before using it, so that data
which breaks a claim fails at the boundary, loudly, rather than deep in the code:

```ts
const response = await pool.request().input('OrderID', sql.Int, 42).execute('Website.GetOrder')
const order = GetOrderSchema.parse(response.recordset?.[0] ?? null)   // a pool made with parseJSON: true
```

[The database module](docs/database-module.md#calling-the-driver-yourself) shows how to read each kind of result, with the
driver's `parseJSON` on and off, and [the generated code](src/Haruspex.Emit.Zod/README.md) how each SQL type arrives.

**Other languages, through JSON Schema.** With `"json-schema"` in `emit`, `generate` also writes `haruspex.jsonschema.json`,
one JSON Schema (draft 2020-12) document from the same claims, so that a service in Python, C#, Go or anything with a JSON
Schema validator checks the same results. It claims what the Zod claims wherever JSON Schema can say it
([the JSON Schema emitter](src/Haruspex.Emit.JsonSchema/README.md)).

**Drift caught in CI.** `haruspex check` fails a build when a change to the SQL would change what a committed module says,
naming each change where it is (`shop.list_orders results[0][].note: became nullable`). Until haruspex is published as a
package, a CI job runs it from a copy of its source, pinned to a commit, and one step checks every module in the repository:

```yaml
- uses: actions/checkout@v7                      # haruspex itself, into .haruspex, which check --all doesn't look in
  with: { repository: sharpblue/haruspex, ref: <commit>, path: .haruspex }
- uses: actions/setup-dotnet@v6
  with: { global-json-file: .haruspex/global.json }
- run: dotnet run --project .haruspex/src/Haruspex.Cli -- check --all   # every haruspex.json below; exit code 1 on a change
```

Pin the two actions by commit too, as haruspex's own workflow does.

**Several modules for several subsystems.** An application whose plugins or sub-applications each have their own interface to
the database gives each its own `haruspex.json`, and each is a module of its own: its own procedures, names, output and
committed contract. Modules share nothing, so two may each have a `getOrder`, and a plugin builds and leaves on its own.
`check --all` checks every module below a folder in one step ([the command line](src/Haruspex.Cli/README.md)).

## What it guarantees, and what it can't see

Every claim haruspex makes holds for every execution: a field it calls non-null is never null, and a bound is never
exceeded. That makes its types an over-approximation, sometimes looser than intuition (`COUNT(*)` is nullable, because past
2,147,483,647 rows it returns NULL under session options the caller controls). When it can't prove a shape it says the shape
is **unknown, with a reason**, and never guesses. It can't see what isn't in the source: dynamic SQL, `EXEC @procedure`, CLR
procedures, system procedures, and objects outside the project unless their DACPACs are given; a config file can declare the
results of any of them, and the generated code still checks them. Nor can it see the session that calls a procedure, whose
`SET` options decide whether some values can be NULL, so it assumes nothing unless `session` in `haruspex.json` declares them
([decision 0038](docs/decisions/0038-the-callers-session-is-declared.md)).

**The more the database declares, the more the types say.** Each declaration becomes a claim: a `CHECK` constraint a value
set or a range, a primary key or unique constraint that a query returns at most one row, a `NOT NULL` column that a value
is there, an alias type a length. A team that designs its schema with care gets that care back in its types, with
nothing to configure. Overrides in `haruspex.json` are for a database that doesn't declare what it means
([the configuration](docs/configuration.md#overrides)).

On the two samples above, with every procedure inferred and no config file, a test writes [the figures](docs/corpus-figures.md):

- **Typed 32 of 38 fields in AdventureWorks.**
- **Typed 170 of 175 fields in WideWorldImporters; the results of 28 of its 129 procedures can't be described, so they have no fields to count.**

[The harvest](docs/corpus-harvest.md) gives the same figures for more public databases, and ranks what they show haruspex
missing.

[What haruspex models](docs/coverage.md) lists every T-SQL construct and built-in function, with what haruspex does with each.

## Status and roadmap

**Pre-release.** The command line, the generated code and the IR, the intermediate representation that holds what haruspex
inferred, will change without notice. Don't depend on it yet.

Today haruspex types what the procedures of a SQL Server database project take and return: for TypeScript as Zod schemas,
with a function for each procedure over the mssql driver, and for any language as JSON Schema. `check` catches drift in CI.

Next, in this order:

1. **Query files.** A team that can't add procedures to the database, or doesn't want to, keeps its queries in `.sql` files in
   the application. Each will be typed against the database project as a procedure is, with a function of its own, and the
   types of its parameters inferred from how the query uses them.
2. **Packages.** A command-line tool installed from the usual package managers, rather than built from source.

Possible later, with no date: checking hand-written schemas against what a procedure can return, for a team that keeps
its own types; checking each consuming application's modules against a change to the database before it is deployed, as
`check` does for one repository today; and `haruspex explain`, to show the reasons behind each claim and each unknown.

Not planned: connecting to a database (haruspex reads source, never a server:
[decision 0001](docs/decisions/0001-infer-from-source-never-a-live-database.md)), building SQL at run time, or generating an
API over the database. Each has tools that do it well, named under [Who it is not for](#who-it-is-not-for).

## Install

It is a command-line tool. No package is published yet; packages for the usual package managers are next on the roadmap.
Until then, build it from source, which needs the [.NET 10 SDK](https://dotnet.microsoft.com/download):

```sh
git clone https://github.com/sharpblue/haruspex.git
cd haruspex
dotnet build
```

The modules it generates import [Zod 4](https://zod.dev), and the functions [mssql](https://www.npmjs.com/package/mssql),
which the application installs itself. The tests compile every generated module in strict mode with
zod 4.6.7, mssql 12.7.14 and TypeScript 7.0.2, so those versions are known to work.

## Quick start

[Five minutes with AdventureWorks](docs/quick-start.md) goes from a fresh clone to the schemas and the function for one of
the sample's procedures, and a script that checks parameters and rows with them. [The documentation's index](docs/README.md)
says what every other page is for, with where to start to use haruspex, to judge whether to trust it, or to contribute.

The repository's example application has a database project and a config file, and calls its procedures through the
functions haruspex writes. From the repository's root:

```sh
cd examples/greenhouse
dotnet run --project ../../src/Haruspex.Cli -- generate
```

This reads `database/` with the settings in `haruspex.json`, and writes `src/db/haruspex.ir.json` (the inferred shapes),
`src/db/haruspex.zod.ts` (the schemas and types) and `src/db/haruspex.mssql.ts` (the functions). For your own database, put a
`haruspex.json` beside its project:

```jsonc
{
  "$schema": "https://github.com/sharpblue/haruspex/raw/main/schemas/haruspex.schema.json",
  "source": "database",
  "out": "src/db",
  "emit": ["zod", "mssql"],
  "schemas": { "Website": { "include": ["Get*"] } }
}
```

and run `generate` from that folder, with the path to your clone of haruspex. `source` is the folder of scripts, the `.sqlproj`
or the `.dacpac`, and [the configuration](docs/configuration.md) lists every other setting.

```sh
dotnet run --project path/to/haruspex/src/Haruspex.Cli -- generate
```

## How it is built

This project is built by agentic engineering. AI coding agents write each change, in small slices, and a second agent reviews
each slice adversarially, trying to break its claims. A person decides what is built and merges every change. Each rule
haruspex relies on about SQL Server is observed on a real server by a test, and the code that relies on it names that test.
[The guide](docs/internals/README.md) explains how it works and why a static analyser is built this way, and
[the decision log](docs/decisions/README.md) records each decision with the alternatives.

## Who it is not for

- **Applications that own their schema.** An ORM such as EF Core, Prisma or Drizzle fits better: there the application
  owns the model, and here the database does.
- **Anyone wanting an API generated over the database**: Data API Builder, PostGraphile or Hasura.
- **Databases other than SQL Server.** For PostgreSQL, pgtyped, sqlc or Kysely with `kysely-codegen`.
- **Code that builds its SQL at run time.** A typed query builder such as Kysely types it where it is built.
- **Databases with no project and no DACPAC.** Extract a DACPAC first, with SqlPackage's `/Action:Extract`.
- **Anyone who needs a supported tool.** This is a personal open-source project with
  [no support commitment](SUPPORT.md).
- **Anyone who won't use code written by AI.** Better to know before you start.

## Licence

[Apache 2.0](LICENSE). Contributions are welcome; see [CONTRIBUTING.md](CONTRIBUTING.md). Report a vulnerability
privately, as [SECURITY.md](SECURITY.md) says.
