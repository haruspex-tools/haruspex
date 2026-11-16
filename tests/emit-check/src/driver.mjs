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
// driver reads it from the pool's config at each request. A call with "check": { "schema", "results", "readJson" } also
// reads its result sets as an application would and parses them with that schema of the module. A rowset is its
// recordset, or [] when its statement did not run; a JSON result is read by readJson, the body of a function of `result`
// (an object holding that result set as its `recordset`), which the test takes from the project's README, so that what is
// proved is the code a reader pastes.
//
// It writes observed.json beside: { "calls": [{ "name", "recordsets": [{ "columns", "rows" }], "error", "check" }] }.
// The connection is given in HARUSPEX_DRIVER_CONFIG, the mssql config as JSON, never on the command line.
import { readFile, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { pathToFileURL } from 'node:url'
import sql from 'mssql'

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
        const { schema, results, readJson } = call.check
        const json = new Function('result', readJson)
        try {
          const values = results.map((encoding, k) =>
            encoding === 'rowset' ? (result.recordsets[k] ?? []) : json({ recordset: result.recordsets[k] }))
          const parsed = schemas[schema].safeParse(values.length === 1 ? values[0] : values)
          entry.check = parsed.success
            ? { success: true, issues: [] }
            : { success: false, issues: parsed.error.issues.slice(0, 10).map((i) => `${i.path.join('.')}: ${i.message}`) }
        } catch (error) {
          // The application's own read threw, before any schema saw the value.
          entry.check = { success: false, issues: [`reading the result threw ${error.message}`] }
        }
      }
    }

    observed.push(entry)
  }
} finally {
  await transaction.rollback()
  await pool.close()
}

await writeFile(path.join(folder, 'observed.json'), JSON.stringify({ calls: observed }, null, 2))
