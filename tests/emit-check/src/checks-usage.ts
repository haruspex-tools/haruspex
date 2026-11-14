// Type-level checks on the Zod generated from tests/fixtures/checks. tsc fails if any of them is wrong.
import type { ListTicketsEvent, ListTicketsRow, TicketRowsRow } from '../generated/checks.zod'

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

// A set of strings is matched under the column's collation ('CLOSED' and 'open   ' pass the CHECK). The column is varchar
// under a collation whose comparison is proven, so a value is mapped to the member it equals ('CLOSED' parses as 'closed')
// and the type is the members...
// @ts-expect-error ...which 'CLOSED' is not.
export const shouted: ListTicketsRow = { ...ticket, status: 'CLOSED' }

// An nchar column's set stays a note: under the Unicode rules a character may compare by its neighbours, which no
// mapping of one character at a time can follow, so the type stays string rather than claiming an enum the data can break.
export const shoutedKind: ListTicketsEvent = { id: 1, kind: 'OPEN' }

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
  sigil: null,
  tag_utf8: null,
  pad_utf8: null,
  muted: null,
  restored: null,
  late: null,
  owner_name: null,
  owner_grade: null,
}
