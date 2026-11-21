// Writes the JSON Schema the pinned Zod's z.toJSONSchema makes of an invented application's schema for a column's JSON: the
// settings of a shop's order. A team may keep its Zod schema as the source and generate the JSON Schema a column is declared
// with from it, so Zod's output is an input haruspex reads (ColumnDeclarationTests.ZodJsonSchema). It uses what such schemas
// use: objects, optional keys, an enum, a discriminated union, a record, a nullable value, strings with formats and lengths,
// integers, a default and an array with a minimum.
import { z } from 'zod'

const Delivery = z.discriminatedUnion('kind', [
  z.object({ kind: z.literal('courier'), carrier: z.string(), eta: z.iso.datetime().optional() }),
  z.object({ kind: z.literal('pickup'), store: z.uuid() }),
])

const Settings = z.object({
  mode: z.enum(['fast', 'slow']),
  note: z.string().min(1).max(200).optional(),
  contact: z.email().nullable(),
  tags: z.record(z.string(), z.number().int()),
  delivery: Delivery,
  attempts: z.number().int().nonnegative(),
  gift: z.boolean().default(false),
  lines: z.array(z.object({ sku: z.string().regex(/^[A-Z]{3}-\d+$/), quantity: z.int() })).min(1),
})

process.stdout.write(JSON.stringify(z.toJSONSchema(Settings), null, 2) + '\n')
