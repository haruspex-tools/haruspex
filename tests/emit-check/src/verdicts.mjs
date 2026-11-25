// Validates each document in a folder against one of haruspex's own schemas, and prints every document's verdict, so that a test
// can compare Ajv's verdict on each with its own validator's and with a reader's. Called by IrJsonSchemaTests,
// IrReaderSchemaAgreementTests and ConfigSchemaTests as `node src/verdicts.mjs <schema> <folder>` (documents.mjs). It prints one JSON object, each
// document's file name mapped to its errors (an empty list when it is valid), and exits with 0 whether or not the documents are
// valid: deciding which should be is the test's. It exits with 1 only when the schema doesn't compile.
//
// The schema is compiled as validate.mjs compiles haruspex's own: draft 2020-12 in strict mode with every error, and ajv-formats
// asserting each format.
import { readFile } from 'node:fs/promises'
import path from 'node:path'
import Ajv2020 from 'ajv/dist/2020.js'
import addFormats from 'ajv-formats'
import { documents as documentsOf } from './documents.mjs'

const [schemaFile, ...given] = process.argv.slice(2)
const ajv = new Ajv2020({ strict: true, allErrors: true })
addFormats(ajv)
let validate
try {
  validate = ajv.compile(JSON.parse(await readFile(schemaFile, 'utf8')))
} catch (error) {
  console.log(`${schemaFile}: ${error.message}`)
  process.exit(1)
}

const verdicts = {}
for (const file of await documentsOf(given)) {
  const valid = validate(JSON.parse(await readFile(file, 'utf8')))
  verdicts[path.basename(file)] = valid ? [] : validate.errors.map((e) => `${e.instancePath} ${e.message}`)
}

console.log(JSON.stringify(verdicts))
