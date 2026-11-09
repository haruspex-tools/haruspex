// Type-level checks on the Zod generated from tests/fixtures/checks. tsc fails if any of them is wrong.
import type { ListTicketsRow, TicketRowsRow } from '../generated/checks.zod'

export const ticket: ListTicketsRow = {
  id: 1,
  code: 'ABCDEFGH',
  status: 'open',
  title: 'A title',
}

// An exact set of numbers (CHECK level = 1 OR level = 2 OR level = 4) is a union of literals...
export const levelTwo: ListTicketsRow = { ...ticket, level: 2 }

// @ts-expect-error ...so 3 is not a level.
export const levelThree: ListTicketsRow = { ...ticket, level: 3 }

// A set of strings is matched under the column's collation ('CLOSED' and 'open   ' pass the CHECK), so the type
// stays string rather than claiming an enum the data can break.
export const shouted: ListTicketsRow = { ...ticket, status: 'CLOSED' }

// An alias type declared NOT NULL makes its column non-null.
// @ts-expect-error code is never absent.
export const noCode: ListTicketsRow = { ...ticket, code: undefined }

// A bigint row value is a string, so its set (none here) and range are strings too.
export const row: TicketRowsRow = {
  id: 1,
  code: 'C',
  alt_code: null,
  status: 'held',
  priority: null,
  score: null,
  level: 4,
  discount: null,
  rate: null,
  big: '9223372036854775807',
  title: 'T',
  note: null,
  region: null,
  tag_utf8: null,
  pad_utf8: null,
  muted: null,
  owner_name: null,
  owner_grade: null,
}
