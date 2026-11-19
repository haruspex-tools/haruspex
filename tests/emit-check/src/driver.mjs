// Calls SQL Server through the mssql driver, as an application does, and writes down what the driver hands over: the
// differential tests' observations of the driver (MssqlDriverObservationTests) and their proof that the generated Zod
// accepts it (MssqlDriverAcceptanceTests) run through this.
//
// It is called with a folder holding driver.json:
//
//   { "module": "<file>.zod.ts" or null,
//     "setup": ["<batch>", …],
//     "calls": [{ "name", "parseJson", "batch" or "procedure" with "parameters", and optionally "timeZone" and "check" }] }
//
// A parameter's value is sent as it is, for the driver to choose its type, or given as { "type", "args", "value" } to send it
// with one of the driver's types, sql[type](...args) with "MAX" for sql.MAX, as a generated function does; a value JSON can't hold is written
// { "js": "NaN" }, and a bigint { "bigInt": "<digits>" }. A table-valued parameter is given as { "table": { "schema", "name",
// "columns": [{ "name", "type", "args" }], "rows": [[…], …] } }, sent as an sql.Table of those columns, in that order. A call with a "timeZone" runs with the process's TZ set to it, which
// is where the driver turns a Date into the server's date and time. A call with "accepts": "<schema>" also parses its
// parameters' values, as an object by name, with that schema of the module, and writes whether it accepted them.
//
// Everything runs in one transaction, which is rolled back at the end, so the setup may create tables and procedures (the
// tests hold the database alone while it runs). Each call runs with the driver's parseJSON setting as it says: the
// driver reads it from the pool's config at each request. A call with "check": { "schema", "results", "readJson" } also
// reads its result sets as an application would and parses them with that schema of the module. A rowset is its
// recordset, or [] when its statement did not run; a JSON result is read by readJson, the body of a function of `result`
// (an object holding that result set as its `recordset`), which the test takes from the project's README, so that what is
// proved is the code a reader pastes.
//
// It writes observed.json beside: { "calls": [{ "name", "recordsets": [{ "columns", "rows" }], "returnValue", "error", "check", "accepted" }] },
// an error as { "code", "number", "message", "preceding" }, "preceding" the numbers of the server's errors the driver's carries.
// The connection is given in HARUSPEX_DRIVER_CONFIG, the mssql config as JSON, never on the command line.
import { readFile, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { pathToFileURL } from 'node:url'
import sql from 'mssql'

const folder = process.argv[2]
const { module, setup, calls } = JSON.parse(await readFile(path.join(folder, 'driver.json'), 'utf8'))
const schemas = module ? await import(pathToFileURL(path.resolve(folder, module)).href) : {}

// The values JSON can't hold, by name.
const special = (value) =>
  value !== null && typeof value === 'object' ? ('js' in value ? { NaN, Infinity }[value.js] : BigInt(value.bigInt)) : value
const plain = (value) => (value !== null && typeof value === 'object' && 'type' in value ? value.value : value)
const zone = process.env.TZ
const driverType = (typed) => (typed.args ? sql[typed.type](...typed.args.map((a) => (a === 'MAX' ? sql.MAX : a))) : sql[typed.type])

const pool = await new sql.ConnectionPool(JSON.parse(process.env.HARUSPEX_DRIVER_CONFIG)).connect()
const transaction = new sql.Transaction(pool)
await transaction.begin()
const observed = []
try {
  for (const batch of setup) await new sql.Request(transaction).batch(batch)

  for (const call of calls) {
    pool.config.parseJSON = call.parseJson
    process.env.TZ = call.timeZone ?? zone
    const request = new sql.Request(transaction)
    const entry = { name: call.name, recordsets: [], error: null, check: null, accepted: null }
    if (call.accepts) {
      const values = Object.fromEntries(Object.entries(call.parameters ?? {}).map(([name, value]) => [name, special(plain(value))]))
      entry.accepted = schemas[call.accepts].safeParse(values).success
    }
    let result
    try {
      if (call.procedure) {
        for (const [name, value] of Object.entries(call.parameters ?? {})) {
          if (value !== null && typeof value === 'object' && 'table' in value) {
            const table = new sql.Table()
            table.schema = value.table.schema
            table.name = value.table.name
            for (const column of value.table.columns) table.columns.add(column.name, driverType(column))
            for (const row of value.table.rows) table.rows.add(...row.map(special))
            request.input(name, table)
          } else if (value !== null && typeof value === 'object' && 'type' in value) {
            request.input(name, driverType(value), special(value.value))
          } else {
            request.input(name, value)
          }
        }
        result = await request.execute(call.procedure)
      } else {
        result = await request.query(call.batch)
      }
    } catch (error) {
      entry.error = {
        code: error.code ?? null,
        number: typeof error.number === 'number' ? error.number : null,
        message: error.message,
        preceding: Array.isArray(error.precedingErrors) ? error.precedingErrors.map((e) => e.number) : [],
      }
    }

    if (result) {
      entry.returnValue = result.returnValue ?? null
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
  // An error that breaks the stream (a value longer than the length it was sent with) aborts the transaction, and the
  // server has rolled it back already.
  await transaction.rollback().catch((error) => {
    if (error.code !== 'EABORT') throw error
  })
  await pool.close()
}

await writeFile(path.join(folder, 'observed.json'), JSON.stringify({ calls: observed }, null, 2))
