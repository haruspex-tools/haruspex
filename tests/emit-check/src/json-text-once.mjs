// Evidence that the JSON text helper builds its content's schema once: building a schema costs a hundred times or more what
// checking a value against it does, so a helper that built it for each value would make a list of rows with a JSON text column
// slow by that much. A count of calls holds on any machine, where a time would not.
//
// Run by EmitCheckTests with a folder holding module.ts, a module haruspex generated with its helper exported as well. Exit
// code 1 when the content is built other than once, or a value is checked wrongly.
import path from 'node:path'
import { pathToFileURL } from 'node:url'
import { z } from 'zod'

// The generated module is TypeScript; Node strips its types (the module uses only erasable syntax).
const { jsonText } = await import(pathToFileURL(path.resolve(process.argv[2], 'module.ts')).href)

let built = 0
const schema = jsonText(() => {
  built++
  return z.array(z.int()).min(1)
})
const failures = []
if (built !== 0) failures.push(`the content was built ${built} times before any value was checked`)

const cases = [['[1]', true], ['[1, 2]', true], ['[]', false], ['not json', false], ['[1.5]', false], ['[3]', true]]
for (const [text, expected] of cases) {
  if (schema.safeParse(text).success !== expected) failures.push(`${text} was ${expected ? 'refused' : 'accepted'}`)
}
if (built !== 1) failures.push(`the content was built ${built} times for ${cases.length} values, not once`)

console.log(`jsonText: ${cases.length} values checked, content built ${built} times`)
for (const failure of failures) console.log(failure)
process.exit(failures.length === 0 ? 0 : 1)
