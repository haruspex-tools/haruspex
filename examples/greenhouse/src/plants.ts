// An application's own code, using the module haruspex generated for its database (src/db/haruspex.zod.ts). Each
// procedure has a schema, to check what the database returns at runtime, and a type, for the code that uses it.
import { GetPlantSchema, ListPlantsSchema, type GetPlant, type GetPlantWaterLogItem, type ListPlantsRow } from './db/haruspex.zod'

/**
 * garden.list_plants, as the mssql driver returns its rows: `recordset` is `unknown` to the application until the schema
 * has checked it. A `date` column arrives as a `Date`.
 */
export function readPlants(recordset: unknown): ListPlantsRow[] {
  return ListPlantsSchema.parse(recordset)
}

/**
 * garden.get_plant returns FOR JSON text, null when no plant has the id. The text is parsed, then checked: an ISO date and
 * time for each entry in its water log, a positive amount with two decimal places.
 */
export function readPlant(json: string | null): GetPlant {
  return GetPlantSchema.parse(json === null ? null : JSON.parse(json))
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

const basil = readPlant('{"id":1,"name":"Basil","kind":"herb","water_log":[{"watered_at":"2026-05-01T08:00:00","litres":0.5}]}')
if (basil !== null) {
  // @ts-expect-error A plant's id is a number: the compiler catches code that treats it as anything else.
  const wrong: string = basil.id
  void wrong
  console.log(`${basil.name}: ${litresGiven(basil).toString()} litres`)
}
