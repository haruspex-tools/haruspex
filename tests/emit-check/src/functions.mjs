// Calls procedures through the functions haruspex generates (haruspex.mssql.ts, with the Zod module beside it, or each module's
// functions file of a split module, with its Zod file and the shared one beside them), as an
// application does: the differential tests' end-to-end check of the database module (GeneratedFunctionTests) and of its
// failures (MssqlFunctionTests) run through this.
//
// It is called with a folder holding functions.json:
//
//   { "setup": ["<batch>", …],
//     "calls": [{ "name", "function", "params" (or absent), "parseJson", "db": "transaction" or "untouchable" }],
//     "functions": ["<functions file>", …] (haruspex.mssql.ts when absent) }
//
// Everything runs in one transaction, which is rolled back at the end, and each call is given the transaction as its
// connection, or, with "untouchable", an object whose request() throws, to prove nothing touched it. A parameter's value
// written { "bigInt": "<digits>" } is passed as a JavaScript bigint. The connection is given in HARUSPEX_DRIVER_CONFIG.
//
// It writes observed.json beside: { "calls": [{ "name", "value" or "error" }] }, an error as { "name", "kind", "message",
// "procedure", "part", "issues", "number", "procName", "isRequestError" }.
import { register } from 'node:module'
import { readFile, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { pathToFileURL } from 'node:url'
import sql from 'mssql'

register('./ts-resolve.mjs', import.meta.url)

const folder = process.argv[2]
const { setup, calls, functions: files = ['haruspex.mssql.ts'] } = JSON.parse(await readFile(path.join(folder, 'functions.json'), 'utf8'))
const functions = {}
for (const file of files) Object.assign(functions, await import(pathToFileURL(path.resolve(folder, file)).href))

const argument = (value) =>
  value !== null && typeof value === 'object' && !Array.isArray(value)
    ? 'bigInt' in value
      ? BigInt(value.bigInt)
      : Object.fromEntries(Object.entries(value).map(([k, v]) => [k, argument(v)]))
    : value

// A value as JSON can hold it: a Date as its ISO string, a bigint as its digits.
const plain = (value) => JSON.parse(JSON.stringify(value, (_, v) => (typeof v === 'bigint' ? v.toString() : v)))

const pool = await new sql.ConnectionPool(JSON.parse(process.env.HARUSPEX_DRIVER_CONFIG)).connect()
const transaction = new sql.Transaction(pool)
await transaction.begin()
const observed = []
try {
  for (const batch of setup) await new sql.Request(transaction).batch(batch)

  for (const call of calls) {
    pool.config.parseJSON = call.parseJson ?? false
    const db = call.db === 'untouchable' ? { request: () => { throw new Error('the connection was touched') } } : transaction
    const entry = { name: call.name }
    try {
      const fn = functions[call.function]
      if (typeof fn !== 'function') throw new Error(`no function ${call.function}`)
      // A function of nothing to hand back returns undefined, which JSON has no form for: written null, as a FOR JSON result over
      // no rows is.
      const value = call.params == null ? await fn(db) : await fn(db, argument(call.params))
      entry.value = value === undefined ? null : plain(value)
    } catch (error) {
      entry.error = {
        name: error.name,
        kind: error.kind ?? null,
        message: error.message,
        procedure: error.procedure ?? null,
        part: error.part ?? null,
        issues: (error.issues ?? []).map((issue) => ({ message: issue.message, path: (issue.path ?? []).map((s) => String(typeof s === 'object' ? s.key : s)) })),
        number: typeof error.number === 'number' ? error.number : null,
        procName: error.procName ?? null,
        isRequestError: error instanceof sql.RequestError,
      }
    }

    observed.push(entry)
  }
} finally {
  await transaction.rollback().catch((error) => {
    if (error.code !== 'EABORT') throw error
  })
  await pool.close()
}

await writeFile(path.join(folder, 'observed.json'), JSON.stringify({ calls: observed }, null, 2))
