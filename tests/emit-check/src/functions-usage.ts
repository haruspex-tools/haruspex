// Type-level checks on the functions generated for tests/fixtures/till (generated/till.mssql.ts): what a caller writes and
// gets back. tsc fails if any is wrong.
import sql from 'mssql'
import { findProducts, ringUp, shiftTimes, type Queryable } from '../generated/till.mssql'

export async function usage(pool: sql.ConnectionPool, transaction: sql.Transaction): Promise<void> {
  // A pool and a transaction are both a connection a function takes.
  const connections: Queryable[] = [pool, transaction]

  // Parameters by their declared names; a bigint as a number, a bigint or the string a rowset hands over.
  const sale = await ringUp(pool, { product_id: 1, quantity: 2, basket_id: '9007199254740993' })

  // An OUTPUT parameter with a default may not come back. Its body never returns a code other than 0, so none is handed back.
  const saleId: number | null | undefined = sale.output.sale_id
  // @ts-expect-error No returnValue for a procedure without a RETURN of another value than 0.
  const code: number = sale.returnValue

  // One result set beside the OUTPUT parameters is result; its rows are the schema's.
  const basket: string | null | undefined = sale.result[0]?.basket_id

  // One result set and nothing else is the result itself.
  const products = await findProducts(pool, { name: 'Tea' })
  const first: unknown = products[0]

  // @ts-expect-error A misspelt parameter is a type error, as well as a ParameterError when the call is made.
  await findProducts(pool, { nmae: 'Tea' })

  // @ts-expect-error A date is an ISO 8601 string, never a Date.
  await shiftTimes(pool, { day: new Date(), opened_at: '2026-11-16T08:30:00' })

  // @ts-expect-error A parameter without a default can't be left out.
  await ringUp(transaction, { quantity: 2, basket_id: 3 })

  void [connections, saleId, code, basket, first]
}
