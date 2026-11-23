// Checks generated JSON Schema documents against the draft 2020-12 meta-schema, and that each compiles under Ajv's strict mode,
// which refuses a keyword it doesn't know, a format no plugin asserts and a pattern that isn't a valid regular expression with
// the u flag. Called by JsonSchemaMetaSchemaTests with the folder it wrote the documents to, and by ReportFormatTests,
// IrJsonSchemaTests and ConfigSchemaTests with one document (documents.mjs); exit code 1 when any fails.
import { readFile } from 'node:fs/promises'
import Ajv2020 from 'ajv/dist/2020.js'
import addFormats from 'ajv-formats'
import { documents } from './documents.mjs'

const files = await documents(process.argv.slice(2))
let failed = 0
for (const file of files) {
  const ajv = new Ajv2020({ strict: true, allErrors: true })
  addFormats(ajv)
  const schema = JSON.parse(await readFile(file, 'utf8'))
  try {
    if (!ajv.validateSchema(schema)) {
      failed++
      console.log(`${file}: not a valid 2020-12 schema: ${ajv.errorsText()}`)
    } else {
      ajv.compile(schema)
    }
  } catch (error) {
    failed++
    console.log(`${file}: ${error.message}`)
  }
}

console.log(`${files.length} document(s), ${failed} failed`)
process.exit(failed === 0 ? 0 : 1)
