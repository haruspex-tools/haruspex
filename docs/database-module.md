# The database module

What `"emit": ["zod", "mssql"]` writes, and how an application uses it: calling a procedure, what a call returns, how its
parameters are checked, how it fails, and where it runs. The last section is for an application that calls the
[mssql](https://www.npmjs.com/package/mssql) driver itself, with the schemas alone. [The configuration](configuration.md#what-is-written-emit)
says how to ask for each file, and the [example application](../examples/greenhouse/README.md) uses both, compiled in CI.

## Two files

`haruspex generate` writes the database module as two files beside the IR:

- `haruspex.zod.ts`, a [Zod](https://zod.dev) schema and a type for each procedure's parameters and results. It imports Zod
  alone, so browser code may import it too.
- `haruspex.mssql.ts`, a function for each procedure, which calls it through the mssql driver. It imports the schemas from
  the file beside it, and the driver.

A function is named after its procedure, with a lower-case first word: `Website.GetOrder` is `getOrder`. The functions' file
imports the schemas as `./haruspex.zod.js`, the name of the file it compiles to, which TypeScript and bundlers map to the
`.ts`. Node's own type stripping doesn't, so the functions run where the application's TypeScript is compiled or bundled
([decision 0058](decisions/0058-the-functions-are-a-file-of-their-own-named-for-their-driver.md)).

## Calling a procedure

A function takes anything with `request()` first (an mssql `ConnectionPool`, a `Transaction`, or a test double) and the
procedure's parameters second, as an object keyed by their declared names without the `@`
([decision 0057](decisions/0057-parameter-keys-are-their-declared-names.md)):

```ts
import { getOrder } from './db/haruspex.mssql'

const { result: order, returnValue } = await getOrder(pool, { OrderID: 42 })
```

It owns no connection. Pooling, retries, timeouts and transactions stay the application's, which wraps the functions rather
than being replaced by them. A procedure with no parameters takes the connection alone.

Every function returns one shape, so a procedure can grow without breaking its callers:

| Key | What it holds |
|---|---|
| `result` | The one result set, checked by the procedure's schema: an array of rows, or a `FOR JSON` document, which is `null` when no rows matched. |
| `result1`, `result2`, … | Each result set, for a procedure that returns more than one. |
| `output` | The `OUTPUT` parameters, by their declared names, for a procedure that has any. |
| `returnValue` | The procedure's return code. |

A procedure that returns no result set has no `result`. A rowset whose statement didn't run, in a branch or after a
`RETURN`, is `[]`. A `FOR JSON` result is read the same whether
the pool was made with the driver's `parseJSON` option or not
([decision 0048](decisions/0048-an-absent-json-result-reads-as-null.md)).

## How the parameters are checked

Before it asks the connection for a request, a function checks its parameters with a strict schema of each one's declared
type, because SQL Server doesn't refuse what that type can't hold: a string longer than `nvarchar(n)` arrives cut to `n`
characters, and a decimal with more places than its scale arrives rounded, without an error. For those, the check in the
application is the only one there is ([decision 0054](decisions/0054-a-parameter-is-checked-against-its-declared-type.md)).
The schema checks:

- **names**: a key the procedure doesn't declare is an error, so a misspelt name fails rather than being ignored;
- **lengths**, in the units the type counts: UTF-16 code units for `nvarchar`, bytes for a `varchar` in a UTF-8 collation;
- **ranges and places**: an integer type's range, and a decimal's precision and scale;
- **dates and times**, as an ISO 8601 string of exactly the form the type holds (`2026-11-16` for a `date`,
  `2026-11-16T08:30:00` for a `datetime2(0)`), never a JavaScript `Date`, which the schema refuses because its meaning would
  depend on the time zone of the process and the driver's `useUTC` setting. To send back a `Date` a rowset handed over, the
  application converts it itself. With the driver's default `useUTC`, such a `Date` holds the stored clock time in its UTC
  fields, so the string is its `toISOString()` without the `Z`, cut to what the type holds (`.slice(0, 10)` for a `date`,
  `.slice(0, 19)` to the second); a `datetimeoffset` takes `toISOString()` as it is. With `useUTC` off, that recipe sends a
  shifted time ([decision 0055](decisions/0055-dates-and-times-are-sent-as-iso-8601-strings.md));
- **`bigint`**, as a safe integer, a JavaScript `bigint`, or the string of digits a rowset hands over.

Each value is then sent with the driver's type for the declared one: `nvarchar(40)` as `sql.NVarChar(40)`, a decimal with its
precision and scale. Every parameter accepts `null`, since a procedure can't declare one `NOT NULL`. One with a default may be
left out, and it is then not sent at all, so that its default applies.

## When a call fails

A function fails in one of three ways, each thrown
([decision 0056](decisions/0056-a-function-throws-three-kinds-of-failure.md)):

- **The parameters were wrong.** A `ParameterError`, whose `name` is `'HaruspexParameterError'` and `kind` is `'parameters'`, thrown
  before the connection is touched. Its message names the procedure and each failing parameter, and `issues` lists them.
  Nothing was sent, so the fault is in the calling code.
- **The database raised an error.** mssql's own `RequestError`, passed through unchanged, with its `number`, `state`,
  `class`, `procName` and `lineNumber`, so existing handling of the driver's errors keeps working.
- **What came back broke the contract.** A `ResultError`, whose `name` is `'HaruspexResultError'` and `kind` is `'result'`, naming
  the procedure, the part (`the results` or `the OUTPUT parameters`) and each issue's path. The database and the application
  are deployed out of step, which no retry fixes: it calls for a deployment, not a second attempt.

Connection failures and timeouts are the driver's, untouched. Each module writes its own error classes, so an application
with several modules tells the errors apart by `name` or `kind`, not `instanceof`:

```ts
try {
  return (await getOrder(pool, { OrderID: id })).result
} catch (error) {
  if (error instanceof Error && error.name === 'HaruspexParameterError') return null   // an id the procedure can't take
  throw error
}
```

## Where the functions run

On the server. They take a connection, and a connection holds the database's credentials. In a Next.js application they are
for route handlers, server actions and server components; the types and schemas, in their own file, may go anywhere.
`"serverOnly": true` in `haruspex.json` starts the functions' file with `import 'server-only'`, so that a build fails where
browser code imports it. It is off by default, since haruspex assumes no framework.

An application whose plugins or sub-applications each need their own interface to the database gives each its own
`haruspex.json`. Each is a module of its own, with its own procedures, names, files and committed contract. No module imports
another, so a procedure two modules select is typed in each, and two modules may each have a `getOrder`.
[The command line](../src/Haruspex.Cli/README.md) says how `generate` and `check` run several at once.

## Calling the driver yourself

An application that calls the driver itself, or through code of its own, uses the schemas alone (`"emit": ["zod"]`). It parses
each result with the procedure's schema before using it, so that data which breaks a claim fails at the boundary, loudly,
rather than deep in the code. The parameters' schema (`GetOrderParamsSchema`) checks what it sends, as the functions do.

SQL Server splits `FOR JSON` output over rows of about 2,000 characters, and the driver joins them back into one row. With the
driver's `parseJSON` option on, as many applications set it, that row is the parsed document, or `null` when no rows matched,
which the schema reads as it is:

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
it parses an array holding each of `result.recordsets`, read as above. A test runs both reads against a real server, through
the driver, in each mode (`MssqlDriverAcceptanceTests`).
