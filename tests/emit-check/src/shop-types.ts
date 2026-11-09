// The application's own schema that tests/fixtures/shop/haruspex.json imports as '@shop/types' (mapped in tsconfig.json):
// what a product's attributes JSON holds. The generated module uses it as it is.
import { z } from 'zod'

export const ProductAttributesSchema = z.string().transform((text) => JSON.parse(text) as unknown).pipe(
  z.object({ colour: z.string().optional(), weightGrams: z.number().optional() }),
)
