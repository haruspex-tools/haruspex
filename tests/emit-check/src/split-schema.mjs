// Checks the JSON Schema documents of a module split into several (a config file's "split"): each document is added to Ajv
// under its file name, as a consumer adds them, and every definition of each is compiled, which fails on a $ref, relative to
// its document's name, that resolves to nothing. Then each case of checks.json in the folder, if there is one, validates a
// value against a definition: [{ "schema": "sales.jsonschema.json#/$defs/Order", "value": …, "valid": false }].
// Called by JsonSchemaModulesTests with the folder; exit code 1 when anything fails.
import { readdir, readFile } from 'node:fs/promises'
import path from 'node:path'
import Ajv2020 from 'ajv/dist/2020.js'
import addFormats from 'ajv-formats'

const folder = process.argv[2]
const ajv = new Ajv2020({ strict: true, allErrors: true })
addFormats(ajv)
const files = (await readdir(folder)).filter((f) => f.endsWith('.jsonschema.json')).sort()
const documents = {}
for (const file of files) {
  documents[file] = JSON.parse(await readFile(path.join(folder, file), 'utf8'))
  ajv.addSchema(documents[file], file)
}

let failed = 0
for (const file of files) {
  for (const name of Object.keys(documents[file].$defs)) {
    try {
      ajv.getSchema(`${file}#/$defs/${name}`)
    } catch (error) {
      failed++
      console.log(`${file}#/$defs/${name}: ${error.message}`)
    }
  }
}

const checks = files.length === 0 ? [] : await readFile(path.join(folder, 'checks.json'), 'utf8').then(JSON.parse, () => [])
for (const check of checks) {
  const valid = ajv.getSchema(check.schema)(check.value)
  if (valid !== check.valid) {
    failed++
    console.log(`${check.schema}: ${JSON.stringify(check.value)} is ${valid ? 'valid' : 'invalid'}, expected ${check.valid ? 'valid' : 'invalid'}`)
  }
}

console.log(`${files.length} document(s), ${checks.length} check(s), ${failed} failed`)
process.exit(failed === 0 ? 0 : 1)
