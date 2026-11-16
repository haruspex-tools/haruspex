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

Many applications reach their database through stored procedures, and nothing at that boundary is typed. A result arrives
as rows the driver can't describe, or as one string of JSON, however deep the `FOR JSON` document inside it. So developers
write the types by hand, and they drift until a field that "can't be null" is null in production. The way in is no safer:
SQL Server cuts a string too long for its parameter, and rounds a decimal with too many places, without an error.

The tool works from the source alone (a folder of `.sql` files, an SSDT `.sqlproj` or a DACPAC). It never connects to a
database, so it needs no credentials and no data. And it follows one rule: **never claim more than the engine guarantees.**

## What an application gets

With `"emit": ["zod", "mssql"]` in `haruspex.json`, `haruspex generate` writes the database module: `haruspex.zod.ts`, a schema
and a type for each procedure's parameters and results, and `haruspex.mssql.ts`, a function for each procedure over the
[mssql](https://www.npmjs.com/package/mssql) driver. A function takes a connection it doesn't own and the parameters by their
declared names, checks them against their declared types before anything is sent, so a value the database would silently
cut or round is caught first ([decision 0054](docs/decisions/0054-a-parameter-is-checked-against-its-declared-type.md)),
and returns what comes back checked and typed as `{ result, output, returnValue }`.

Queries the application keeps in its own `.sql` files get the same: `"queries": "sql"` in `haruspex.json` types each file
against the database project, with its parameters' types inferred from how it uses them (`where o.order_id = @order_id` makes
`@order_id` the column's type), and gives it a function named for the file, which sends it through `sp_executesql`.

The functions belong on the server, since they take the connection and its credentials; the schemas and types, in their own
file, may go to the browser. [The database module](docs/database-module.md) has the detail: dates, return shapes and
`serverOnly` for Next.js.

## Two examples on Microsoft's samples

Each example is written for this page over the real tables of a Microsoft sample kept in
[`tests/corpus`](tests/corpus/README.md). A test fails if the code shown differs from what haruspex writes, and the
differential tests call each through its generated function on SQL Server and check what comes back against the claims.

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

And its function, after the helpers every module of functions begins with:

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

`quantity` is at least 1 and `standardCost` at least 0 from the sample's `CHECK` constraints, `name` is at most 50 characters
from its alias type `Name`, and `unit` is exactly three characters because `nchar(3)` arrives padded.

## Other ways to use it

- **The types and parsers alone**, for a team that calls the driver itself: leave `"mssql"` out of `emit`, and parse each
  result with its schema, `GetOrderSchema.parse(response.recordset?.[0] ?? null)` with the driver's `parseJSON` on
  ([how to read each kind of result](docs/database-module.md#calling-the-driver-yourself)).
- **Other languages**: `"json-schema"` in `emit` writes the same claims as one JSON Schema document, for any language with a
  validator ([the JSON Schema emitter](src/Haruspex.Emit.JsonSchema/README.md)).
- **Drift caught in CI**: `haruspex check` fails a build when a change to the SQL would change what a committed module says,
  naming each change (`shop.list_orders results[0][].note: became nullable`).
- **Several modules**: plugins or sub-applications each have a `haruspex.json` of their own, sharing nothing, and
  `check --all` checks them all in one step ([the command line](src/Haruspex.Cli/README.md), with a CI job to copy).

## What it guarantees, and what it can't see

Every claim haruspex makes holds for every execution: a field it calls non-null is never null, and a bound is never
exceeded. That makes its types an over-approximation, sometimes looser than intuition (`COUNT(*)` is nullable, because past
2,147,483,647 rows it returns NULL under session options the caller controls). When it can't prove a shape it says the shape
is **unknown, with a reason**, and never guesses. It can't see what isn't in the source: dynamic SQL, `EXEC @procedure`, CLR
and system procedures, and objects outside the project unless their DACPACs are given; a config file can declare their
results. Nor can it see the caller's session `SET` options, unless `session` in `haruspex.json` declares them
([decision 0038](docs/decisions/0038-the-callers-session-is-declared.md)).

The more the database declares, the more the types say: a `CHECK` constraint becomes a value set or a range, and a key a
query filters on in full means at most one row. Overrides in `haruspex.json` are for what a database doesn't declare.

At run time the generated functions check both ends, so a wrong parameter or a result that no longer matches its schema
fails loudly, with an error saying which, rather than reaching the application's code; database errors stay the driver's.
[When a call fails](docs/database-module.md#when-a-call-fails) says how to tell them apart.

On the two samples above, a test writes [the figures](docs/corpus-figures.md):

- **Typed 32 of 38 fields in AdventureWorks.**
- **Typed 170 of 175 fields in WideWorldImporters; the results of 28 of its 129 procedures can't be described, so they have no fields to count.**

[The harvest](docs/corpus-harvest.md) measures more databases; [what haruspex models](docs/coverage.md) lists every construct.

## Status and roadmap

**Pre-release.** The command line, the generated code and the IR (the record of what haruspex inferred) will change without
notice. Don't depend on it yet.

Available now, as well as procedures: **query files**, queries kept in the application's own `.sql` files, typed against the
database project as procedures are, each with a function, its parameters' types inferred from how the query uses them
([the configuration](docs/configuration.md#query-files-queries)).

Next: **packages**, rather than building from source.

Possible later, with no date: checking hand-written schemas against a procedure, checking each consuming application before
a database change is deployed, and `haruspex explain`. Not planned: connecting to a database
([decision 0001](docs/decisions/0001-infer-from-source-never-a-live-database.md)), building SQL at run time, or an API over
the database.

## Install

It is a command-line tool, not yet published as a package. Build it from source, which needs the
[.NET 10 SDK](https://dotnet.microsoft.com/download):

```sh
git clone https://github.com/sharpblue/haruspex.git
cd haruspex
dotnet build
```

The application installs [Zod 4](https://zod.dev) and [mssql](https://www.npmjs.com/package/mssql) itself. The tests compile
every generated module in strict mode with zod 4.6.7, mssql 12.7.14 and TypeScript 7.0.2.

## Quick start

[Five minutes with AdventureWorks](docs/quick-start.md) goes from a fresh clone to the schemas and the function for one of
the sample's procedures. [The example application](examples/greenhouse/README.md) calls its procedures through the
functions haruspex wrote for it, and [the configuration](docs/configuration.md) shows a `haruspex.json` for your own
database. [The documentation's index](docs/README.md) says what every other page is for.

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
