# The database module

What `"emit": ["zod", "mssql"]` writes, and how an application uses it: calling a procedure, what a call returns, how its
parameters are checked, how it fails, where it runs, and the functions of the application's own query files. The last section is for an application that calls the
[mssql](https://www.npmjs.com/package/mssql) driver itself, with the schemas alone. [The configuration](configuration.md#what-is-written-emit)
says how to ask for each file, and the [example application](../examples/greenhouse/README.md) uses both, compiled in CI. What a
function returns is the database's shape; [the application's own types](layers.md) shows the step that makes it the
application's.

## Two files

`haruspex generate` writes the database module as two files beside the IR:

- `haruspex.zod.ts`, a [Zod](https://zod.dev) schema and a type for each procedure's and query file's parameters and results. It imports Zod
  alone, so browser code may import it too.
- `haruspex.mssql.ts`, a function for each procedure and query file, which runs it through the mssql driver. It imports the schemas from
  the file beside it, and the driver.

A function is named after its procedure, with a lower-case first word: `Website.GetOrder` is `getOrder`. The functions' file
imports the schemas as `./haruspex.zod.js`, the name of the file it compiles to, which TypeScript and bundlers map to the
`.ts`. Node's own type stripping doesn't, so the functions run where the application's TypeScript is compiled or bundled
([decision 0058](decisions/0058-the-functions-are-a-file-of-their-own-named-for-their-driver.md)).

A config file that splits its output ([`split`](configuration.md#splitting-the-output-into-modules-split)) writes these two
files for each module, named after it: `sales.zod.ts` and `sales.mssql.ts`, whose functions import `./sales.zod.js`, with what
two or more modules' schemas share in `shared.zod.ts`. Import a function from its module's file, and a type from the file of the
module it belongs to; every name is the one the unsplit module gives.

## Calling a procedure

A function takes anything with `request()` first (an mssql `ConnectionPool`, a `Transaction`, or a test double) and the
procedure's parameters second, as an object keyed by their declared names without the `@`
([decision 0057](decisions/0057-parameter-keys-are-their-declared-names.md)):

```ts
import { getOrder } from './db/haruspex.mssql'

const order = await getOrder(pool, { OrderID: 42 })
```

It owns no connection. Pooling, retries, timeouts and transactions stay the application's, which wraps the functions rather
than being replaced by them. A procedure with no parameters takes the connection alone.

A function returns what its procedure hands back, checked by the procedure's schema
([decision 0060](decisions/0060-a-call-returns-its-one-result-itself.md)). For a procedure with one result set and nothing else,
that is the result itself: an array of rows, or a `FOR JSON` document, which is `null` when no rows matched. A procedure that also
has `OUTPUT` parameters or a return code, that returns several result sets, or whose sets the config file names, gives an object
naming each part:

| Key | What it holds |
|---|---|
| `result` | The one result set. |
| `result1`, `result2`, … | Each result set, for a procedure that returns more than one. |
| each set's name | Each result set, under the name the config file's `sets` gives it, however many there are: `{ order, lines }` in place of `result` or `result1`, `result2` ([decision 0070](decisions/0070-a-procedure-s-result-sets-are-named-all-or-none.md)). |
| `output` | The `OUTPUT` parameters, by their declared names, for a procedure that has any. |
| `returnValue` | The procedure's return code, for a procedure whose body has a `RETURN` with a value other than 0 or NULL. Without one, the code is always 0 (observed), so it isn't handed back. |

A procedure that returns no result set has no `result`, and one that hands back nothing at all returns nothing. The shape follows
the procedure: when it gains an `OUTPUT` parameter or a `RETURN` with a value, its function's shape changes, `haruspex check` says
so, and the compiler finds each call to change. So it does when a procedure of one unnamed set gains a second; one whose sets are
named already returns an object, so a set added and named is one more key. A rowset whose statement didn't run, in a branch or after a
`RETURN`, is `[]`. A result set after those the procedure lists, which a database deployed ahead of the application may
return, is ignored, as a key a result's type doesn't list is, unless `"objects": "strict"` asks to refuse both; a set the
procedure always returns that doesn't arrive fails the call either way
([decision 0063](decisions/0063-a-function-ignores-result-sets-after-those-it-knows.md)). A `FOR JSON` result is read the same whether
the pool was made with the driver's `parseJSON` option or not
([decision 0048](decisions/0048-an-absent-json-result-reads-as-null.md)).

## Working against two databases

Generated with a baseline (`haruspex generate --baseline`, or `"baseline"` in the config file), the module works against the
database before a change is deployed and after it, so a release of the application can go out before the database's change, or
after it ([Generating against a baseline](command-line.md#generating-against-a-baseline)). Each file's header says so:

```ts
// Combined with a baseline (Shop.dacpac): results accept what either database returns, and parameters take only what both accept.
```

What changes for the code that calls the functions:

- **A key one database doesn't return is optional** (`row.channel` is `string | null | undefined`), and so is a key one leaves
  out when null where the other sends the null. Read it with a default, `row.channel ?? 'web'`, in the step that makes the row
  the application's own ([the application's own types](layers.md)); once the baseline moves on, the key is required again, and
  the compiler shows the defaults no longer needed.
- **A value set, a length or a range is the wider of the two**, and a value of another kind in each database is a union of both.
- **A result set only one database returns may be missing**: `[]` for rows, `null` for a `FOR JSON` result, as a set whose
  statement didn't run is.
- **A parameter only one procedure has is left out** while it has a default there, and a parameter's type is the narrower of the
  two, so the function never sends what one of them refuses.
- **A procedure whose change can't be bridged has no function**, until the change is shipped in two releases: a call of it
  doesn't compile, which the run's error explains (HAR6033).

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
  the procedure, the part (`the results` or `the OUTPUT parameters`) and each issue's path. In a procedure of several result
  sets the path starts with the set, counted from 1, and a set's count of rows is said in rows:
  `sales.order_sheet, the results: result set 2: Expected exactly one row`. The database and the application are deployed out
  of step, which no retry fixes: it calls for a deployment, not a second attempt.

Connection failures and timeouts are the driver's, untouched. Each module writes its own error classes, so an application
with several modules tells the errors apart by `name` or `kind`, not `instanceof`:

```ts
try {
  return await getOrder(pool, { OrderID: id })
} catch (error) {
  if (error instanceof Error && error.name === 'HaruspexParameterError') return null   // an id the procedure can't take
  throw error
}
```

## Testing with a double

A function takes anything with `request()`, so a test can hand it a double rather than a pool. The double's `request()`
returns an object with `input()` and `output()`, whose results the function ignores, and `execute()`, which the function
calls for a procedure and for a query file alike (a query file runs through `sp_executesql`). What `execute()` resolves to
is what the function reads, and it must have all three of the driver's fields:

- `recordsets`: every result set, in order, each an array of rows (or, for a `FOR JSON` result, the one row the driver
  makes of it). The function reads the sets from here, never from `recordset`, the first set alone, which hand-written
  doubles often give instead: with only `recordset`, every call fails before any schema is applied;
- `output`: the `OUTPUT` parameters, by their declared names, `{}` when there are none;
- `returnValue`: the return code, 0 unless the test is about another.

Typing the result as the driver's `IProcedureResult` makes the compiler ask for each. A double for a procedure with an `OUTPUT`
parameter, as a test in this repository runs it against a generated function:

```ts
import type sql from 'mssql'
import type { Queryable } from './db/haruspex.mssql'

/** A connection whose every call returns these result sets, OUTPUT parameters and return code. */
export function returning(sets: object[][], output: Record<string, unknown> = {}, returnValue = 0): Queryable {
  const result: sql.IProcedureResult<object> = {
    recordsets: sets as sql.IRecordSet<object>[],
    recordset: sets[0] as sql.IRecordSet<object>,
    output,
    returnValue,
    rowsAffected: sets.map((rows) => rows.length),
  }
  const request = { input: (): void => undefined, output: (): void => undefined, execute: async () => result }
  return { request: () => request as unknown as sql.Request }
}

const sale = await ringUp(returning([[{ basket_id: '7', discount: 0, gift: false, token: null, weight: null, ratio: null }]], { sale_id: 12 }), {
  product_id: 1,
  quantity: 2,
  basket_id: 7,
})
// { result: [{ basket_id: '7', … }], output: { sale_id: 12 } }
```

What the double returns is still checked by the procedure's schemas, so a double that drifts from the database fails the
way the database would, with a `ResultError`.

## Where the functions run

On the server. They take a connection, and a connection holds the database's credentials. In a Next.js application they are
for route handlers, server actions and server components; the types and schemas, in their own file, may go anywhere.
`"serverOnly": true` in `haruspex.json` starts the functions' file with `import 'server-only'`, so that a build fails where
browser code imports it. It is off by default, since haruspex assumes no framework.

**In tests.** With `"serverOnly": true`, a test that imports the functions' file, itself or through the code under test,
imports `server-only` too, and a test runner resolves it as Node does: `Cannot find package 'server-only'` when it isn't
installed, and when it is, the package's own error, `This module cannot be imported from a Client Component module`, since
it is empty only under the `react-server` condition, which Next.js sets for server code. So the tests need one of these
(both checked with Vitest 5.1):

- `server-only` installed (`npm install --save-dev server-only`) and resolved under the `react-server` condition, beside
  Vite's own conditions for server code. The condition then applies to every package the tests import, so a package with a
  `react-server` build, React among them, resolves to that build:

  ```ts
  // vitest.config.ts
  import { defaultServerConditions } from 'vite'
  import { defineConfig } from 'vitest/config'

  export default defineConfig({ ssr: { resolve: { conditions: [...defaultServerConditions, 'react-server'] } } })
  ```

- or `server-only`, installed or not, resolved to an empty module of the application's own (`export {}`), which changes
  nothing else:

  ```ts
  // vitest.config.ts
  import { fileURLToPath } from 'node:url'
  import { defineConfig } from 'vitest/config'

  export default defineConfig({ resolve: { alias: { 'server-only': fileURLToPath(new URL('./test/empty.ts', import.meta.url)) } } })
  ```

An application whose plugins or sub-applications each need their own interface to the database gives each its own
`haruspex.json`. Each is a module of its own, with its own procedures, names, files and committed contract. No module imports
another, so a procedure two modules select is typed in each, and two modules may each have a `getOrder`.
[The command line](../src/Haruspex.Cli/README.md) says how `generate` and `check` run several at once.

## Query files

A query file the config file names in `queries` ([the configuration](configuration.md#query-files-queries)) has a function as a
procedure has, named for the file by the same rules: `sql/get_order_card.sql` is `getOrderCard`. Procedures and query files share
the module's names, so one that would take another's is an error naming both ([HAR4303](diagnostics/HAR4303.md),
[HAR5201](diagnostics/HAR5201.md)). It is called the same way, and returns as a procedure's does:

```ts
import { getOrderCard } from './db/haruspex.mssql'

const card = await getOrderCard(pool, { order_id: 42 })
```

The function sends the file's text, as it is written, through `sp_executesql`, declaring each parameter with the type haruspex
inferred for it, or the one a comment in the file declares (`-- @param @label nvarchar(40)`), so the query runs with the types
it was typed with. Each value is checked and sent as a procedure's is, and the server converts it to that declared type, so a date
sent as its ISO 8601 string is a `date` in the query, not text. A parameter whose type nothing decides can't be declared, and
stops the run until the file declares it ([HAR5202](diagnostics/HAR5202.md)). Every parameter accepts `null`.

What can fail is what a procedure's call can, with a query file's path where a procedure's name would be: a `ParameterError`'s
and a `ResultError`'s `procedure`, and their messages, name the file (`sql/get_order_card.sql: @order_id: …`). The database's
error is mssql's `RequestError`. A query file has no `OUTPUT` parameters and no return code: a batch run through `sp_executesql` may
`RETURN`, but SQL Server refuses `RETURN` with a value there (error 178), so haruspex refuses such a file when it reads it
([HAR1017](diagnostics/HAR1017.md)), the code is always 0, and a query file of one result set returns that result itself. The text is part of the module, so
`haruspex check` fails when a query file has changed since the module was generated.

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
it parses an array holding each of `result.recordsets`, read as above, and only those the procedure lists: the tuple refuses
any more, so an application that reads the sets itself takes as many as the tuple has, as the functions do, to survive a set
added after them. A test runs both reads against a real server, through
the driver, in each mode (`MssqlDriverAcceptanceTests`).
