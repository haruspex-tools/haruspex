// Calls SQL Server through the mssql driver, as an application does, and writes down what the driver hands over: the
// differential tests' observations of the driver (MssqlDriverObservationTests) and their proof that the generated Zod
// accepts it (MssqlDriverAcceptanceTests) run through this.
//
// It is called with a folder holding driver.json:
//
//   { "module": "<file>.zod.ts" or null,
//     "setup": ["<batch>", …],
//     "calls": [{ "name", "parseJson", "batch" or "procedure" with "parameters", and optionally "check" }] }
//
// Everything runs in one transaction, which is rolled back at the end, so the setup may create tables and procedures (the
// tests hold the database alone while it runs). Each call runs with the driver's parseJSON setting as it says: the
// driver reads it from the pool's config at each request. A call with "check": { "schema", "results" } also reads its
// result sets as an application would (below, and the project's README) and parses them with that schema of the module.
//
// It writes observed.json beside: { "calls": [{ "name", "recordsets": [{ "columns", "rows" }], "error", "check" }] }.
// The connection is given in HARUSPEX_DRIVER_CONFIG, the mssql config as JSON, never on the command line.
import { readFile, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { pathToFileURL } from 'node:url'
import sql from 'mssql'

// The name SQL Server gives the one column of a top-level FOR JSON result set, which the driver recognises it by.
const jsonColumn = 'JSON_F52E2B61-18A1-11d1-B105-00805F49916B'

// A result set as the application reads it. With parseJSON on, the driver hands over a JSON result as one row, the
// parsed document or null; without it, as one row holding the text, '' when the query matched no rows. A result set that
// did not arrive (its statement did not run) reads as no rows, or as null for JSON: the IR's reader convention.
const read = (recordset, encoding, parseJSON) => {
  if (encoding === 'rowset') return recordset ?? []
  if (parseJSON) return recordset?.[0] ?? null
  const text = recordset?.[0]?.[jsonColumn]
  return text ? JSON.parse(text) : null
}

const folder = process.argv[2]
const { module, setup, calls } = JSON.parse(await readFile(path.join(folder, 'driver.json'), 'utf8'))
const schemas = module ? await import(pathToFileURL(path.resolve(folder, module)).href) : {}

const pool = await new sql.ConnectionPool(JSON.parse(process.env.HARUSPEX_DRIVER_CONFIG)).connect()
const transaction = new sql.Transaction(pool)
await transaction.begin()
const observed = []
try {
  for (const batch of setup) await new sql.Request(transaction).batch(batch)

  for (const call of calls) {
    pool.config.parseJSON = call.parseJson
    const request = new sql.Request(transaction)
    const entry = { name: call.name, recordsets: [], error: null, check: null }
    let result
    try {
      if (call.procedure) {
        for (const [name, value] of Object.entries(call.parameters ?? {})) request.input(name, value)
        result = await request.execute(call.procedure)
      } else {
        result = await request.query(call.batch)
      }
    } catch (error) {
      entry.error = { code: error.code ?? null, message: error.message }
    }

    if (result) {
      entry.recordsets = result.recordsets.map((recordset) => ({ columns: Object.keys(recordset.columns), rows: [...recordset] }))
      if (call.check) {
        const { schema, results } = call.check
        const values = results.map((encoding, k) => read(result.recordsets[k], encoding, call.parseJson))
        const parsed = schemas[schema].safeParse(values.length === 1 ? values[0] : values)
        entry.check = parsed.success
          ? { success: true, issues: [] }
          : { success: false, issues: parsed.error.issues.slice(0, 10).map((i) => `${i.path.join('.')}: ${i.message}`) }
      }
    }

    observed.push(entry)
  }
} finally {
  await transaction.rollback()
  await pool.close()
}

await writeFile(path.join(folder, 'observed.json'), JSON.stringify({ calls: observed }, null, 2))
