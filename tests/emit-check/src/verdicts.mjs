// Runs values through a generated module's schemas and compares what each schema decides with what a test expects: the
// evidence that a schema refuses the forms SQL Server and the mssql driver never hand over, as well as accepting the ones they
// do. Called by ZodVerdictTests as `node src/verdicts.mjs <folder>`, the folder holding module.ts, a generated module, and
// cases.json, [{ "schema", "value", "accepted" }], each value as a caller would hand it to the schema. Exit code 1 when any
// schema decides other than expected.
import { readFile } from 'node:fs/promises'
import path from 'node:path'
import { pathToFileURL } from 'node:url'

const folder = process.argv[2]
const cases = JSON.parse(await readFile(path.join(folder, 'cases.json'), 'utf8'))

// The generated module is TypeScript; Node strips its types (the module uses only erasable syntax).
const schemas = await import(pathToFileURL(path.resolve(folder, 'module.ts')).href)

const wrong = cases.filter(({ schema, value, accepted }) => schemas[schema].safeParse(value).success !== accepted)
for (const { schema, value, accepted } of wrong)
  console.log(`${schema}: ${JSON.stringify(value)} was ${accepted ? 'refused' : 'accepted'}`)
console.log(`${cases.length} values checked, ${wrong.length} decided wrongly`)
process.exit(wrong.length === 0 ? 0 : 1)
