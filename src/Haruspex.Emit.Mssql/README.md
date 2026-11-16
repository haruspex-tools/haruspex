# Haruspex.Emit.Mssql

Writes the database module's functions from a [haruspex IR](https://github.com/sharpblue/haruspex/blob/main/src/Haruspex.Ir/README.md)
document: one TypeScript function for each procedure and query file, which runs it through the
[mssql](https://www.npmjs.com/package/mssql) driver, the driver most Node applications reach SQL Server with. A function checks its parameters with the procedure's
parameters schema, binds each with the driver's type, executes, and checks each result set and the `OUTPUT` parameters with
their schemas:

```ts
const { result } = await getOrder(pool, { order_id: 42 })
```

The functions are a file of their own (`haruspex.mssql.ts`), which imports the schemas and types from the schema module beside
it (`haruspex.zod.ts`): so an application's browser code can import the types and schemas, and the functions stay on the
server, where the connection and its credentials are. `MssqlOptions.ServerOnly` adds `import 'server-only'`, which makes a Next.js build
fail where browser code imports the functions.

## How data flows through it

```mermaid
flowchart LR
    document["IrDocument"] --> plan["FunctionPlanner: a MssqlFunction each"]
    options["MssqlOptions: the schema module, server-only, reserved words"] --> plan
    plan --> print["MssqlPrinter"]
    print --> module["haruspex.mssql.ts"]
```

The plan decides each function's name, how each parameter is bound (`DriverTypes`) and how each result set is read; the printer
is the only stage that knows TypeScript and the driver's API. Two stages, not the Zod emitter's three, because there is no
model of expressions to lower: a function's code is the same for every procedure but for its names, bindings and reads.

## Example

```csharp
var document = IrJsonReader.Read(File.ReadAllText("haruspex.ir.json"));
if (MssqlEmitter.Emit(document, new MssqlOptions { SchemaModule = "./haruspex.zod.js" }) is Succeeded<string> { Value: var module })
    File.WriteAllText("haruspex.mssql.ts", module);
```

## What it guarantees

- **A function takes a connection; it never owns one.** Its first argument is anything with `request()`, an mssql
  `ConnectionPool`, a `Transaction`, or a test double (`Queryable`). Pooling, retries and transactions stay the application's.
- **Parameters are checked before the connection is touched**, and a failure is thrown as a `ParameterError` naming the
  procedure and each parameter. A parameter left out is not bound, so its default applies: bound as `undefined`, the driver
  would send NULL, which overrides it (observed). An `OUTPUT` parameter without a default is always bound, since SQL Server
  refuses a call that leaves it out.
- **Each value is sent with the driver's type for the declared one** (`DriverTypes`): `nvarchar(40)` as `sql.NVarChar(40)`, a
  decimal with its precision and scale, and a date or time as text, which SQL Server converts exactly in any session
  ([decision 0055](https://github.com/sharpblue/haruspex/blob/main/docs/decisions/0055-dates-and-times-are-sent-as-iso-8601-strings.md)).
- **The database's error is the driver's own**, a `RequestError` with its number, state, class, procedure and line, passed
  through unchanged. **A result that breaks its schema** is a `ResultError` naming the procedure, the part (the results, or the
  `OUTPUT` parameters) and each issue's path. Each error has a stable `name` and `kind`, so code that handles errors from several
  modules tells them apart without importing any
  ([decision 0056](https://github.com/sharpblue/haruspex/blob/main/docs/decisions/0056-a-function-throws-three-kinds-of-failure.md)).
- **One shape of return for every procedure**: `result` for one result set, `result1`, `result2` and so on for several,
  `output` for the `OUTPUT` parameters, and `returnValue`, the return code. A `FOR JSON` result is read as decision 0048 fixes,
  whether the pool parses JSON or not.
- **A query file's function runs its text through `sp_executesql`**: the text as the IR holds it, and each parameter declared
  by its name and its type's `sql`, so the query runs with the types it was inferred with, and each value is bound beside them
  by name, converted by the server to its declared type. A parameter with no type to declare, or named `@stmt` or `@params`
  (`sp_executesql`'s own), is refused (HAR5202). Its failures name its path where a procedure's would name the procedure, and
  its `returnValue` is always 0, since the server refuses `RETURN` with a value in such a batch (error 178, observed).
- **It checks through Standard Schema**, the interface Zod, Valibot and ArkType implement, never through Zod's own methods, so
  the functions are the same whichever validator wrote the schemas, by the names `SchemaModuleNames` gives.
- **It is self-contained.** Its errors and helpers are written into it, so a module needs no package of haruspex's at run time,
  and several modules in one application share nothing.

## What it deliberately doesn't do

- **No naming.** A function's name is its procedure's settled name with a lower-case first word (`GetOrder` is `getOrder`); a
  name that is reserved, or one the module already binds, is refused (`MssqlCodes`). A parameter's key is its declared name
  without its `@`.
- **No connection, no retries, no caching, no transactions.** The application wraps the functions rather than being replaced by
  them.
- **No SQL.** It binds from what the IR says of each type, its kind, format and facets, never from its spelling.

It is part of [haruspex](https://github.com/sharpblue/haruspex), and pre-release: its API and its output will change. Its chapter
of [the guide](https://github.com/sharpblue/haruspex/blob/main/docs/internals/README.md) is
[Emitting code](https://github.com/sharpblue/haruspex/blob/main/docs/internals/11-emitting-code.md).
