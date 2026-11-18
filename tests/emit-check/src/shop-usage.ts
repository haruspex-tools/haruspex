// Type-level checks on the Zod generated from tests/fixtures/shop with its haruspex.json. tsc fails if any is wrong.
import type { Basket, BasketItem, BasketLineRow, ListProducts, AdminSales, AdminStock, AdminStockProductsRow, PriceListItem, ProductsUnder } from '../generated/shop/haruspex.zod'

// "nullable": false on customerName: the key is required and never null, though a left join reads it.
export const basket: Basket = { id: 1, customerName: 'Ada', lines: [{ position: 1, sku: 'A-1', quantity: 2 }] }

// @ts-expect-error customerName can't be left out any more.
export const anonymous: Basket = { id: 2 }

// A WITHOUT_ARRAY_WRAPPER result reads as null when no row matches.
export const none: Basket = null

// The lines' type is named by its path override.
export const line: BasketItem = { position: 1, sku: 'A-1', quantity: 2 }

// "values": "exact" makes the status a union of its literals...
export const product: ListProducts[number] = {
  product_id: 1,
  sku: 'A-1',
  name: 'Widget',
  status: 'active',
  attributes: { colour: 'red' },
  price: 9.99,
}

// @ts-expect-error ...so a status in another case is no longer a status.
export const shouted: ListProducts[number] = { ...product, status: 'ACTIVE' }

// # → Row, and shop_admin's prefix and its own replacement.
export const one: BasketLineRow = { basketId: 1, position: 1, quantity: 2 }
export const sold: AdminSales = [{ sku: 'A-1', price: 9.99 }]

// Named sets are an object of them by name, and the rows of each are named after it.
export const stock: AdminStock = { products: [{ sku: 'A-1', price: 9.99 }], count: [{ products: '1' }] }

// @ts-expect-error Named sets aren't a tuple any more.
export const positional: AdminStock = [[{ sku: 'A-1', price: 9.99 }], [{ products: '1' }]]

// A shape two procedures share is one type under each name, declared once under the name the config file gave it.
export const listed: PriceListItem = { sku: 'A-1', price: 9.99 }
export const stocked: AdminStockProductsRow = listed

// A query file's one set, named by its -- @sets comment, is a key of its own too.
export const cheap: ProductsUnder['products'] = []
