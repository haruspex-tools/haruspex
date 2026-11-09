// Runs real output through the generated Zod: the differential tests' acceptance check
// (Haruspex.DifferentialTests/ZodAcceptanceTests.cs) writes a folder holding a fixture's generated module and
// cases.json, the output of every run of the fixture, and calls this with the folder. Exit code 1 when any schema
// rejects its run's output.
//
// cases.json is { "module": "<file>.zod.ts", "cases": [{ "run", "schema", "value" }] }. A value is what a caller
// would hand the schema: a FOR JSON result's text exactly as SQL Server wrote it (so JSON.parse rounds its numbers
// as it would in an application), [] or null for an absent result (the IR's reader convention), and rows as the
// mssql driver delivers them, with dates as { "$date": "…" } turned into Date objects here.
import { readFile } from 'node:fs/promises'
import path from 'node:path'
import { pathToFileURL } from 'node:url'

const folder = process.argv[2]
const revive = (_, value) =>
  value !== null && typeof value === 'object' && !Array.isArray(value) && Object.keys(value).length === 1 && '$date' in value
    ? new Date(value.$date)
    : value
const { module, cases } = JSON.parse(await readFile(path.join(folder, 'cases.json'), 'utf8'), revive)
// The generated module is TypeScript; Node strips its types (the module uses only erasable syntax).
const schemas = await import(pathToFileURL(path.resolve(folder, module)).href)

let rejected = 0
for (const { run, schema, value } of cases) {
  const result = schemas[schema].safeParse(value)
  if (!result.success) {
    rejected++
    console.log(`${run}: ${schema} rejected the output:`)
    for (const issue of result.error.issues.slice(0, 10)) {
      // Zod 4 leaves the value out of an issue unless asked, so it is looked up by the issue's path.
      const got = issue.path.reduce((v, key) => (v == null ? v : v[key]), value)
      console.log(`  ${issue.path.join('.')}: ${issue.message} (got ${JSON.stringify(got)})`)
    }
  }
}

console.log(`${cases.length} run(s), ${rejected} rejected`)
process.exit(rejected === 0 ? 0 : 1)
