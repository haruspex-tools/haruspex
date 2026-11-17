// Runs a generated function (till.ring_up's, from generated/till.mssql.ts) against the test double in test-double.ts, which
// docs/database-module.md shows, and checks that its result and OUTPUT parameters come back from the double, and that a
// double giving only recordset, as hand-written ones often do, fails the call. Driven by EmitCheckTests.
import assert from 'node:assert/strict'
import { register } from 'node:module'

register('./ts-resolve.mjs', import.meta.url)
const { returning } = await import('./test-double.ts')
const { ringUp } = await import('../generated/till.mssql.ts')

const row = { basket_id: '7', discount: 0, gift: false, token: null, weight: null, ratio: null }
const params = { product_id: 1, quantity: 2, basket_id: 7 }

assert.deepEqual(await ringUp(returning([[row]], { sale_id: 12 }), params), { result: [row], output: { sale_id: 12 } })

// What the double returns is checked as the database's would be.
await assert.rejects(ringUp(returning([[row, row]]), params), { name: 'HaruspexResultError', message: 'till.ring_up, the results: Expected exactly one row' })

const onlyRecordset = { request: () => ({ input() {}, output() {}, execute: async () => ({ recordset: [row] }) }) }
await assert.rejects(ringUp(onlyRecordset, params), TypeError)
