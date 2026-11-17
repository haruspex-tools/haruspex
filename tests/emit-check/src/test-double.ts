// The test double docs/database-module.md shows ("Testing with a double"), for the functions generated for tests/fixtures/till:
// what a generated function needs from request().execute(). It must return recordsets (every result set, in order), output
// (the OUTPUT parameters) and returnValue (the return code); a double that returns only recordset fails every call. Typing
// the result as the driver's IProcedureResult makes the compiler ask for each. src/double.mjs runs a function against it.
import type sql from 'mssql'
import type { Queryable } from '../generated/till.mssql'

/** A connection whose every call returns these result sets, OUTPUT parameters and return code. */
export function returning(sets: object[][], output: Record<string, unknown> = {}, returnValue = 0): Queryable {
  const result: sql.IProcedureResult<object> = {
    recordsets: sets as sql.IRecordSet<object>[],
    recordset: sets[0] as sql.IRecordSet<object>,
    output,
    returnValue,
    rowsAffected: sets.map((rows) => rows.length),
  }
  const request = { input: (): void => undefined, output: (): void => undefined, execute: async () => result }
  return { request: () => request as unknown as sql.Request }
}
