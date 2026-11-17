# An application that uses a generated module

A small TypeScript application for an invented greenhouse, which keeps its plants and a log of their watering in SQL
Server. It shows what a team does with haruspex's output: `haruspex generate`, run in this folder, reads the database
project in `database`, and the application's own query file in `queries`, with the settings in `haruspex.json`, and writes the
database module: `src/db/haruspex.zod.ts`, a schema and a type for each procedure and query file, and `src/db/haruspex.mssql.ts`,
a function for each, which runs it through the [mssql](https://www.npmjs.com/package/mssql) driver, with `src/db/haruspex.mapping.ts`,
the mapping helper. The application's own code, `src/plants.ts`, calls the functions, and uses the types to write the code that
handles what they return; `src/watering.ts` makes the application's own types from them.

```sh
dotnet run --project ../../src/Haruspex.Cli -- generate
npm ci
npm run check
```

The module is committed, as a team would commit it, and a test (`ExampleAppTests`) checks that it is what haruspex writes
today and that the application compiles against it, with the TypeScript, zod and mssql versions in `package-lock.json`. The
application is only compiled, never run: there is no database here to call. The differential tests call every fixture's
procedures through the functions haruspex generates, on a real server, with the same driver (`GeneratedFunctionTests`).

What to look at:

- `getPlant(pool, { plant_id: 1 })`: a function takes the pool (or a transaction) and the procedure's parameters by their
  declared names, checks them before anything is sent, and checks what comes back with the procedure's schema. It returns
  the procedure's one result itself, since `garden.get_plant` has no `OUTPUT` parameter and no return code. The functions belong on the server, which holds the pool and its credentials.
- `garden.get_plant` returns `FOR JSON`, so what `getPlant` returns is the document, or `null` when no plant has the id; the pool's
  `parseJSON` makes no difference to it. A plant never watered has no `water_log` key at all, and the type says so
  (`water_log?`).
- `queries/thirsty_plants.sql` is a query the application keeps in its own file, and `thirstyPlants` its function, named
  from the file. Its parameters are the variables it uses without declaring: `@kind`, compared with `p.kind`, takes that
  column's type, `nvarchar(10)`; `@since` is compared with an aggregate, from which no type is inferred, so a comment in the file
  declares it (`-- @param @since datetime2(0)`). The function sends the file's text through `sp_executesql` with those types.
- `plantOrNothing` tells a failure apart by its name, HaruspexParameterError, without importing anything; the database's
  own errors are mssql's `RequestError`, passed through as they are.
- `garden.list_plants` returns rows, which the mssql driver delivers with `date` columns as `Date`, so `planted` is a
  `Date | null`.
- `src/watering.ts` is the step where the database's shape becomes the application's
  ([the application's own types](../../docs/layers.md)): `toPlant` turns the row `getPlant` returns into a `Plant` whose
  `history` is never absent and whose amounts are `Litres`, and the type it takes accounts for every key of the row, so a
  column the procedure gains is a compile error there.
- `kind` is limited by a CHECK constraint, but it is an `nvarchar` column, which the database compares under its collation:
  where `HERB` can pass a check for `herb`, the value that arrives may be either, so the module notes the values rather
  than claiming an enum. [The configuration](../../docs/configuration.md#the-databases-collation-collation) says when a set
  becomes an enum, and how to declare one exact.
