// Type-level checks on the Zod generated from tests/fixtures/orders. tsc fails if any of them is wrong.
// Each one pins down a rule of the IR as TypeScript sees it, so a change to the emitter that broke the rule
// would fail here as well as in the snapshots.
import type {
  GetOrder,
  GetOrderRow,
  ListCustomerOrdersRow,
  ListCustomersRow,
  ListOrdersRow,
} from '../generated/haruspex.zod'

// A top-level FOR JSON query that matches no rows returns no row at all; the reader convention makes that
// null for a single-object result (and [] for a list).
export const noOrder: GetOrder = null

// FOR JSON without INCLUDE_NULL_VALUES leaves a null key out, so every nullable key may be absent...
export const minimalOrder: GetOrderRow = {
  id: 1,
  reference: '6F9619FF-8B86-D011-B42D-00C04FC964FF',
  placedAt: '2026-11-09T10:00:00',
  status: 'open',
  total: 12.5,
  isPriority: false,
  customer: { id: 7, name: 'A customer' },
}

// ...but is never null.
export const orderWithNull: GetOrderRow = {
  ...minimalOrder,
  // @ts-expect-error FOR JSON never writes null without INCLUDE_NULL_VALUES.
  shippedAt: null,
}

// Dates, times and GUIDs in JSON are strings in a known format. They are not converted to Date, which
// would shift an offset-less date or datetime into the reader's time zone.
export const orderWithTimes: GetOrderRow = {
  ...minimalOrder,
  shippedAt: '2026-11-10T09:30:00+01:00',
  deliveryTime: '14:00:00',
  salesperson: { id: 3, name: 'A salesperson', hiredOn: '2020-02-01' },
}

export const orderWithDateObject: GetOrderRow = {
  ...minimalOrder,
  // @ts-expect-error JSON holds a datetime2 as a string, never a Date.
  placedAt: new Date(),
}

// A nested object from dotted aliases is required when one of its fields is never null (customer.id)...
// @ts-expect-error customer is always present.
export const orderWithoutCustomer: GetOrderRow = { ...minimalOrder, customer: undefined }

// ...and may be absent when every field can be null (salesperson, from a left join).
export const orderWithSalesperson: GetOrderRow = { ...minimalOrder, salesperson: { name: 'A salesperson' } }

// A FOR JSON subquery nests an array, absent when it has no rows.
export const orderWithLines: GetOrderRow = {
  ...minimalOrder,
  lines: [{ id: 1, quantity: 2, unitPrice: 3.5, product: { sku: 'SKU-1', name: 'A product' } }],
}

// INCLUDE_NULL_VALUES keeps a null key, so it is present and may be null...
export const customerOrders: ListCustomerOrdersRow = { customerId: 1, name: 'A customer', email: null, orders: null }

// @ts-expect-error ...and never absent.
export const customerOrdersWithoutEmail: ListCustomerOrdersRow = { customerId: 1, name: 'A customer', orders: null }

// Rowset columns are always present; the mssql driver returns dates as Date and bigint as a string.
export const customer: ListCustomersRow = { id: 1, name: 'A customer', email: null, region: null, createdAt: new Date() }

export const order: ListOrdersRow = {
  order_id: 1,
  placed_at: new Date(),
  status: 'open',
  total: 1,
  customer_name: 'A customer',
  product_legacy_code: '9007199254740993',
  // A left join makes a NOT NULL column nullable.
  salesperson_name: null,
  salesperson_hired_on: null,
}
