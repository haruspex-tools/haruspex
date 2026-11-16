// Runs real output through the generated JSON Schema: the differential tests' acceptance check for the second backend
// (Haruspex.DifferentialTests/JsonSchemaAcceptanceTests.cs) writes a folder holding a fixture's generated document and
// cases.json, the output of every run of the fixture, and calls this with the folder. Exit code 1 when any definition
// rejects its run's output.
//
// cases.json is { "document": "<file>.jsonschema.json", "cases": [{ "run", "definition", "value" }] }. A value is JSON as the
// document describes it: a FOR JSON result's text exactly as SQL Server wrote it, null for an absent one, and rows as FOR JSON
// would write each of their values.
//
// Ajv runs in strict mode on draft 2020-12, with ajv-formats asserting each format, so a date, a date and time with an offset,
// and a GUID are checked by their format as well as their pattern.
import { readFile } from 'node:fs/promises'
import path from 'node:path'
import Ajv2020 from 'ajv/dist/2020.js'
import addFormats from 'ajv-formats'

const folder = process.argv[2]
const { document, cases } = JSON.parse(await readFile(path.join(folder, 'cases.json'), 'utf8'))
const ajv = new Ajv2020({ strict: true, allErrors: true })
addFormats(ajv)
ajv.addSchema(JSON.parse(await readFile(path.join(folder, document), 'utf8')), document)

let rejected = 0
for (const { run, definition, value } of cases) {
  const validate = ajv.getSchema(`${document}#/$defs/${definition}`)
  if (validate === undefined) {
    rejected++
    console.log(`${run}: the document has no definition ${definition}`)
  } else if (!validate(value)) {
    rejected++
    console.log(`${run}: ${definition} rejected the output:`)
    for (const error of validate.errors.slice(0, 10)) {
      // The value at the error's place, found by its JSON pointer.
      const got = error.instancePath.split('/').slice(1).reduce((v, key) => (v == null ? v : v[key.replaceAll('~1', '/').replaceAll('~0', '~')]), value)
      console.log(`  ${error.instancePath || '/'}: ${error.message} (got ${JSON.stringify(got)})`)
    }
  }
}

console.log(`${cases.length} run(s), ${rejected} rejected`)
process.exit(rejected === 0 ? 0 : 1)
