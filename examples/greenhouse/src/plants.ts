// An application's own code, using the database module haruspex generated for its database: src/db/haruspex.zod.ts, with a
// schema and a type for each procedure, and src/db/haruspex.mssql.ts, with a function for each, which calls it through the
// mssql driver and checks what goes in and what comes back. The functions belong on the server, which holds the pool.
import sql from 'mssql'
import { getPlant, listPlants } from './db/haruspex.mssql'
import type { GetPlant, GetPlantWaterLogItem, ListPlantsRow } from './db/haruspex.zod'

/**
 * The application's pool. With `parseJSON` on, the driver joins the rows SQL Server splits a FOR JSON result over and parses
 * them; the functions read a FOR JSON result the same with it off.
 */
export function connect(config: sql.config): Promise<sql.ConnectionPool> {
  return new sql.ConnectionPool({ ...config, parseJSON: true }).connect()
}

/**
 * garden.list_plants returns rows, checked by its schema before the function returns them. A `date` column arrives as a
 * `Date`, as the driver delivers it.
 */
export async function plants(pool: sql.ConnectionPool): Promise<ListPlantsRow[]> {
  const { result } = await listPlants(pool)
  return result
}

/** The types are the application's own from here on: no haruspex type appears in its code. */
export function litresGiven(plant: NonNullable<GetPlant>): number {
  // A plant never watered has no water_log key at all, which FOR JSON leaves out rather than writing an empty array.
  return (plant.water_log ?? []).reduce((total: number, entry: GetPlantWaterLogItem) => total + entry.litres, 0)
}

export function label(plant: ListPlantsRow): string {
  // planted is nullable, so the compiler insists on the case where it is null.
  return plant.planted === null ? plant.name : `${plant.name}, planted ${plant.planted.toDateString()}`
}

export async function report(pool: sql.ConnectionPool): Promise<void> {
  // A procedure's parameters are an object of its declared names, checked before anything is sent.
  const { result: basil } = await getPlant(pool, { plant_id: 1 })
  // garden.get_plant may find no plant (FOR JSON over no rows), so the compiler insists on the case where it is null.
  if (basil === null) return
  // @ts-expect-error A plant's id is a number: the compiler catches code that treats it as anything else.
  const wrong: string = basil.id
  void wrong
  console.log(`${basil.name}: ${litresGiven(basil).toString()} litres`)
}

/**
 * The three ways a call fails, told apart by name, without importing anything from the module: wrong parameters (nothing was
 * sent), the database's own error (mssql's RequestError, as it is), and a result its schema refused (the database and the
 * application are deployed out of step).
 */
export async function plantOrNothing(pool: sql.ConnectionPool, id: number): Promise<GetPlant> {
  try {
    return (await getPlant(pool, { plant_id: id })).result
  } catch (error) {
    if (error instanceof Error && error.name === 'HaruspexParameterError') return null
    throw error
  }
}
