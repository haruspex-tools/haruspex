# An application that uses a generated module

A small TypeScript application for an invented greenhouse, which keeps its plants and a log of their watering in SQL
Server. It shows what a team does with haruspex's output: `haruspex generate`, run in this folder, reads the database
project in `database` with the settings in `haruspex.json`, and writes `src/db/haruspex.zod.ts`. The application's own code,
`src/plants.ts`, imports the schemas to check what the database returns and the types to write the code that uses it.

```sh
dotnet run --project ../../src/Haruspex.Cli -- generate
npm ci
npm run check
```

The module is committed, as a team would commit it, and a test (`ExampleAppTests`) checks that it is what haruspex writes
today and that the application compiles against it, with the TypeScript and zod versions in `package-lock.json`. The
application is only compiled, never run: there is no database here to call.

What to look at:

- `garden.get_plant` returns `FOR JSON` text, so the application parses it and then checks it with the schema the module
  exports for it. A plant never watered has no `water_log` key at all, and the type says so (`water_log?`).
- `garden.list_plants` returns rows, which the mssql driver delivers with `date` columns as `Date`, so `planted` is a
  `Date | null`.
- `kind` is limited by a CHECK constraint, but under a case-insensitive collation the database would accept `HERB` as
  `herb`, so the module notes the values rather than claiming an enum.
