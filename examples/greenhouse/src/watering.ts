// The application's own types for a plant and its watering history, and the one step that makes them from what the database module
// returns. The database's names and shapes stop here: no code past this file sees water_log, watered_at or an absent key.
import type sql from 'mssql'
import type { Exhaustive } from './db/haruspex.mapping'
import { getPlant } from './db/haruspex.mssql'
import { GetPlantSchema, type GetPlantRow } from './db/haruspex.zod'

/** Litres of water: a number the compiler won't accept where another figure is wanted, nor this one in its place. */
export type Litres = number & { readonly unit: 'litres' }

/** One watering, as the application records it. */
export interface Watering {
  /** The greenhouse's own clock time, as the log keeps it: 2026-11-16T08:30:00. */
  readonly at: string
  readonly amount: Litres
}

/** A plant, in the application's words. */
export interface Plant {
  readonly id: number
  readonly name: string
  readonly history: readonly Watering[]
}

/** What toPlant does with each key of garden.get_plant's row. A key the procedure gains stops the build here. */
type PlantRow = Exhaustive<GetPlantRow, {
  id: 'used'
  name: 'used'
  kind: 'ignored: the watering history treats every kind alike'
  water_log: { watered_at: 'used'; litres: 'used' }
}>

export function toPlant({ id, name, water_log }: PlantRow): Plant {
  return {
    id,
    name,
    // FOR JSON leaves out the key of a plant never watered; to the application, that is an empty history.
    history: (water_log ?? []).map(({ watered_at, litres }) => ({ at: watered_at, amount: litres as Litres })),
  }
}

export async function plant(pool: sql.ConnectionPool, id: number): Promise<Plant | null> {
  const row = await getPlant(pool, { plant_id: id })
  return row === null ? null : toPlant(row)
}

/** The same step on the generated schema, for code that calls the driver itself and parses with the schema alone. */
export const PlantSchema = GetPlantSchema.transform((row) => (row === null ? null : toPlant(row)))
