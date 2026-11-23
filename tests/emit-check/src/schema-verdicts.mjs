// Runs values through a generated module's schemas, or a generated JSON Schema document's definitions, and compares what each
// decides with what a test expects: the evidence that a schema refuses the forms SQL Server and the mssql driver never hand
// over, as well as accepting the ones they do. Called by ZodVerdictTests as `node src/schema-verdicts.mjs <folder>`, the folder
// holding cases.json, [{ "schema" or "definition", "value", "accepted" }], and what the cases name: module.ts, a generated Zod
// module, for a "schema" (an export of it); document.json, a generated JSON Schema document, for a "definition" (one of its
// $defs), validated by Ajv as accept-json-schema.mjs validates one (strict, draft 2020-12, formats asserted). Each value is as a
// caller would hand it over. Exit code 1 when anything decides other than expected.
import { existsSync } from 'node:fs'
import { readFile } from 'node:fs/promises'
import path from 'node:path'
import { pathToFileURL } from 'node:url'
import Ajv2020 from 'ajv/dist/2020.js'
import addFormats from 'ajv-formats'

const folder = process.argv[2]
const cases = JSON.parse(await readFile(path.join(folder, 'cases.json'), 'utf8'))

// The generated module is TypeScript; Node strips its types (the module uses only erasable syntax).
const moduleFile = path.join(folder, 'module.ts')
const schemas = existsSync(moduleFile) ? await import(pathToFileURL(moduleFile).href) : {}

const ajv = new Ajv2020({ strict: true, allErrors: true })
addFormats(ajv)
const documentFile = path.join(folder, 'document.json')
if (existsSync(documentFile)) ajv.addSchema(JSON.parse(await readFile(documentFile, 'utf8')), 'document.json')

const decides = ({ schema, definition, value }) =>
  schema !== undefined ? schemas[schema].safeParse(value).success : ajv.getSchema(`document.json#/$defs/${definition}`)(value)

const wrong = cases.filter((c) => decides(c) !== c.accepted)
for (const { schema, definition, value, accepted } of wrong)
  console.log(`${schema ?? definition}: ${JSON.stringify(value)} was ${accepted ? 'refused' : 'accepted'}`)
console.log(`${cases.length} values checked, ${wrong.length} decided wrongly`)
process.exit(wrong.length === 0 ? 0 : 1)
