// Validates documents against a JSON Schema: the command line's reports, its JSON (schemas/haruspex.output.schema.json) and
// SARIF (tests/sarif/sarif-2.1.0.json), against their schemas. Called by ReportFormatTests as
// `node src/validate.mjs <schema> <folder>`, each document a .json file in the folder (documents.mjs); exit code 1 when a
// document doesn't validate or the schema doesn't compile.
//
// Ajv runs on draft 2020-12, the draft both schemas name, with ajv-formats asserting each format. Strict mode is on for
// haruspex's own schema, so a keyword it doesn't know fails; the SARIF schema is OASIS's, written for a laxer reader (it gives
// "type" beside keywords for another type), so it is compiled without strict mode, which changes nothing it accepts.
import { readFile } from 'node:fs/promises'
import path from 'node:path'
import Ajv2020 from 'ajv/dist/2020.js'
import addFormats from 'ajv-formats'
import { documents as documentsOf } from './documents.mjs'

const [schemaFile, ...given] = process.argv.slice(2)
const documents = await documentsOf(given)
const ours = path.basename(schemaFile).startsWith('haruspex.')
const ajv = new Ajv2020({ strict: ours, allErrors: true })
addFormats(ajv)
const validate = ajv.compile(JSON.parse(await readFile(schemaFile, 'utf8')))

let failed = 0
for (const file of documents) {
  if (!validate(JSON.parse(await readFile(file, 'utf8')))) {
    failed++
    console.log(`${file}: ${ajv.errorsText(validate.errors, { separator: '\n  ' })}`)
  }
}

console.log(`${documents.length} document(s), ${failed} failed`)
process.exit(failed === 0 ? 0 : 1)
