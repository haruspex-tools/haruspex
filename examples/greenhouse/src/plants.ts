// An application's own code, using the module haruspex generated for its database (src/db/haruspex.zod.ts). Each
// procedure has a schema, to check what the database returns at runtime, and a type, for the code that uses it. It calls
// the database through the mssql driver, as most Node applications on SQL Server do.
import sql from 'mssql'
import { GetPlantSchema, ListPlantsSchema, type GetPlant, type GetPlantWaterLogItem, type ListPlantsRow } from './db/haruspex.zod'

/**
 * The application's pool. With `parseJSON` on, the driver joins the rows SQL Server splits a FOR JSON result over and
 * parses them, so each such procedure's document arrives as one row; it is set once, here, for every call.
 */
export function connect(config: sql.config): Promise<sql.ConnectionPool> {
  return new sql.ConnectionPool({ ...config, parseJSON: true }).connect()
}

/**
 * garden.list_plants returns rows. The driver's types don't know what they hold until the schema has checked them; a
 * `date` column arrives as a `Date`.
 */
export async function listPlants(pool: sql.ConnectionPool): Promise<ListPlantsRow[]> {
  const result = await pool.request().execute('garden.list_plants')
  return ListPlantsSchema.parse(result.recordset)
}

/**
 * garden.get_plant returns FOR JSON: one row holding the document, parsed by the driver, or null when no plant has the id.
 * The schema checks it: an ISO date and time for each entry in its water log, a positive amount with two decimal places.
 */
export async function getPlant(pool: sql.ConnectionPool, plantId: number): Promise<GetPlant> {
  const result = await pool.request().input('plant_id', sql.Int, plantId).execute('garden.get_plant')
  return GetPlantSchema.parse(result.recordset[0])
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
  const basil = await getPlant(pool, 1)
  // getPlant may find no plant, so the compiler insists on the case where it is null.
  if (basil === null) return
  // @ts-expect-error A plant's id is a number: the compiler catches code that treats it as anything else.
  const wrong: string = basil.id
  void wrong
  console.log(`${basil.name}: ${litresGiven(basil).toString()} litres`)
}
