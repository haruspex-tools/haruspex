// Type-level checks on the modules generated from tests/fixtures/market, whose haruspex.json splits its output by schema. tsc
// fails if any is wrong.
import type { OrderTotal } from '../generated/market/shared.zod'
import type { GetProductOrdersRow, ListStaffRow } from '../generated/market/shop.zod'
import type { GetOrder, ListOrdersRow } from '../generated/market/sales.zod'
import type { ListUsersRow } from '../generated/market/admin.zod'
import type { OrderCount } from '../generated/market/queries.zod'
import { getOrder } from '../generated/market/sales.mssql'
import { getProduct } from '../generated/market/shop.mssql'

// A declared name a module shares is the shared file's export, and every type of its shape is it, in whichever module.
export const total: OrderTotal = { order_id: 1, total: 9.5 }
export const fromSales: ListOrdersRow = total
export const fromShop: GetProductOrdersRow = total
export const order: GetOrder = [total]

// A shape no name declares is shared too, by its types' own names in each module.
export const user: ListUsersRow = { user_id: 1, login: 'ada' }
export const staff: ListStaffRow = user

// A query file that names no module is in the default one.
export const counted: OrderCount = [{ orders: '3' }]

// Each module's functions are its own, and return its own types.
export const calls: [typeof getOrder, typeof getProduct] = [getOrder, getProduct]

// @ts-expect-error A module imports what it shares; it never re-exports it.
export type { OrderTotalSchema } from '../generated/market/shop.zod'
