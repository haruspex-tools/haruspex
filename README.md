# haruspex

A tool that carries a SQL Server database's types into TypeScript by static analysis. It reads a database project's source,
infers what each stored procedure returns, `FOR JSON` documents included, and generates [Zod](https://zod.dev) schemas and
types for application code to use. It is pre-release: see [Status and roadmap](#status-and-roadmap).

## The problem

Many applications reach their database through stored procedures, and nothing at that boundary is typed. A procedure's
result reaches the application as rows the driver can't describe, or as one string of JSON: SQL Server reports a `FOR JSON`
query as a single `nvarchar(max)` column, however deep the document inside it. So developers write the types by hand. Nothing
checks them against the SQL, and they drift as the procedures change, until a field that "can't be null" is null in
production.

The tool works from the source alone (a folder of `.sql` files, an SSDT `.sqlproj` or a DACPAC). It never connects to a
database, so it needs no credentials and no data. And it follows one rule: **never claim more than the engine guarantees.**

## Two examples on Microsoft's samples

Each example below is written for this page over the real tables of one of Microsoft's sample databases, which the
repository keeps in [`tests/corpus`](tests/corpus/README.md). A test infers each over the whole sample and fails if the Zod
shown here differs from what haruspex writes, and the differential tests run each on SQL Server and check every value that
comes back against the claims.

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

/** Website.GetOrder: returns JSON, null when no rows match. */
export const GetOrderSchema = GetOrderRowSchema.nullable()
export type GetOrder = z.infer<typeof GetOrderSchema>
```

Each line has its reason in the SQL. `email`, `brand` and `purchaseOrderNumber` are optional rather than nullable, because
their columns allow NULL and `FOR JSON` leaves a null key out rather than writing `null`. `lines` has at least one item when
it is there, and is absent for an order with none, because a `FOR JSON` subquery over no rows is NULL, not `[]`. The
salesperson is always there: the join is on a foreign key that is never NULL. The whole result is one object or null, because
the filter is on the primary key and `WITHOUT_ARRAY_WRAPPER` writes a single object.

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

/** Production.GetProductTree: returns JSON, null when no rows match. */
export const GetProductTreeSchema = GetProductTreeRowSchema.nullable()
export type GetProductTree = z.infer<typeof GetProductTreeSchema>
```

The schema knows what the database declares: `quantity` is at least 1 and `standardCost` at least 0 from the sample's
`CHECK` constraints, `name` is at most 50 characters from its alias type `Name`, and `unit` is exactly three characters
because `nchar(3)` arrives padded. A part with no components has no `components` key.

## What it does for you today

**It types an application's reads of procedures.** `haruspex generate` writes a module with a Zod schema and a type for each
procedure's results: rowsets as the [mssql](https://www.npmjs.com/package/mssql) driver returns them, and `FOR JSON` text as
the document inside it. It takes the database's source, as a folder of scripts, a `.sqlproj` or a DACPAC, and a
`haruspex.json` beside it saying which procedures are the application's interface; [the configuration](docs/configuration.md)
also covers names, overrides and declared results.

**It validates results at the seam, at run time.** The types are only as good as the data that arrives, so the application
parses each result with the schema before using it. A wrong claim, such as one in an override a person wrote, fails there,
loudly, rather than deep in the code. SQL Server splits `FOR JSON` output over rows of about 2,000 characters, and the
[mssql](https://www.npmjs.com/package/mssql) driver joins them back into one row. With the driver's `parseJSON` option on,
as many applications set it, that row is the parsed document, or `null` when no rows matched, which the schema reads as it
is:

```ts
import sql from 'mssql'
import { GetOrderSchema, type GetOrder } from './db/haruspex.zod'

// pool was made with { ...config, parseJSON: true }.
export async function getOrder(pool: sql.ConnectionPool, orderId: number): Promise<GetOrder> {
  const result = await pool.request().input('OrderID', sql.Int, orderId).execute('Website.GetOrder')
  return GetOrderSchema.parse(result.recordset?.[0] ?? null)
}
```

Without `parseJSON`, the row holds the text, under the name SQL Server gives the column of `FOR JSON` output, and the text is
empty when no rows matched:

```ts
const text: string = result.recordset?.[0]?.['JSON_F52E2B61-18A1-11d1-B105-00805F49916B'] ?? ''
return GetOrderSchema.parse(text === '' ? null : JSON.parse(text))
```

Either way, a `FOR JSON` result with no rows is `null`, a list as much as one object, and its type says so. A result set
whose statement didn't run, in a branch or after a `RETURN`, isn't sent at all, so `result.recordset` is then `undefined`:
that is what `?.` and `?? null` are for, and a schema whose summary says "when it does not run" needs them. Rows from a plain
`SELECT` are `result.recordset`, or `[]` when it didn't run. A procedure with several result sets has one schema, a tuple:
it parses an array holding each of `result.recordsets`, read as above.

[`examples/greenhouse`](examples/greenhouse/README.md) is a small application that does this with a module haruspex wrote,
compiled in CI.

## Status and roadmap

**Pre-release.** The command line, the generated code and the IR, the intermediate representation that holds what haruspex
inferred, will change without notice. Don't depend on it yet.

Today haruspex types what the procedures of a SQL Server database project return, for TypeScript through Zod, and catches
drift in CI: `haruspex check` fails a build when a change to the SQL would change what a committed module says, naming each
change where it is (`shop.list_orders results[0][].note: became nullable`). One step checks every module in a repository:

```yaml
- run: dotnet run --project path/to/haruspex/src/Haruspex.Cli -- check --all   # every haruspex.json below; exit code 1 on a change
```

None of the following exists yet; each is a job the generated types and the IR are designed to make possible:

- **Checking hand-written schemas** against what a procedure can return, for a team that keeps its own types.
- **Seeing what a change does to every caller**, from the database's side: each consuming application's modules checked
  against a change to the database before it is deployed, as `check` does for one repository today.
- **Asking why a field is typed as it is.** `haruspex explain` will show the reasons behind each claim and each unknown.
- **Typing other languages**, through JSON Schema emitted from the same IR.
- **Parameters**, next: strict schemas for what a procedure is sent, not only what it returns.

## What it guarantees, and what it can't see

Every claim haruspex makes holds for every execution: a field it calls non-null is never null, and a bound is never
exceeded. That makes its types an over-approximation, sometimes looser than intuition (`COUNT(*)` is nullable, because past
2,147,483,647 rows it returns NULL under session options the caller controls). When it can't prove a shape it says the shape
is **unknown, with a reason**, and never guesses. It can't see what isn't in the source: dynamic SQL, `EXEC @procedure`, CLR
procedures, system procedures, and objects outside the project unless their DACPACs are given; a config file can declare the
results of any of them, and the generated code still checks them. Nor can it see the session that calls a procedure, whose
`SET` options decide whether some values can be NULL, so it assumes nothing unless `session` in `haruspex.json` declares them
([decision 0038](docs/decisions/0038-the-callers-session-is-declared.md)).

On the two samples above, with every procedure inferred and no config file, a test writes [the figures](docs/corpus-figures.md):

- **Typed 32 of 38 fields in AdventureWorks.**
- **Typed 170 of 175 fields in WideWorldImporters; the results of 28 of its 129 procedures can't be described, so they have no fields to count.**

[The harvest](docs/corpus-harvest.md) gives the same figures for more public databases, and ranks what they show haruspex
missing.

[What haruspex models](docs/coverage.md) lists every T-SQL construct and built-in function, with what haruspex does with each.

## Install

It is a command-line tool. No package is published yet; packages for the usual package managers are planned. Until
then, build it from source, which needs the [.NET 10 SDK](https://dotnet.microsoft.com/download):

```sh
git clone https://github.com/sharpblue/haruspex.git
cd haruspex
dotnet build
```

## Quick start

The repository's example application has a database project and a config file. From the repository's root:

```sh
cd examples/greenhouse
dotnet run --project ../../src/Haruspex.Cli -- generate
```

This reads `database/` with the settings in `haruspex.json`, and writes `src/db/haruspex.ir.json` (the inferred shapes) and
`src/db/haruspex.zod.ts` (the schemas and types). For your own database, put a `haruspex.json` beside its project:

```jsonc
{
  "$schema": "https://github.com/sharpblue/haruspex/raw/main/schemas/haruspex.schema.json",
  "source": "database",
  "out": "src/db",
  "schemas": { "Website": { "include": ["Get*"] } }
}
```

and run `generate` from that folder, with the path to your clone of haruspex. `source` is the folder of scripts, the `.sqlproj`
or the `.dacpac`.

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
- **Anyone who needs a supported tool.** This is a personal open-source project with no support commitment.
- **Anyone who won't use code written by AI.** Better to know before you start.

## Licence

[Apache 2.0](LICENSE). Contributions are welcome; see [CONTRIBUTING.md](CONTRIBUTING.md).
