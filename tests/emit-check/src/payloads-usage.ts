// Type-level checks on the Zod generated from tests/fixtures/payloads, built with the JSON constructors. tsc fails if
// any of them is wrong.
import type { AuthorStatsRow, GetAuthorAuthor, ListBooksRow, ReviewsByBookRow } from '../generated/payloads.zod'

export const book: ListBooksRow = {
  id: 1,
  // json_array of values never null is a tuple: each position its own type...
  pair: [1, 12.5],
  // ...and with a value that can be null (dropped by ABSENT ON NULL), an array of the elements' types.
  labels: ['A title'],
  // NULL ON NULL keeps every position, possibly null.
  slots: ['A title', null, null],
  // json_object(... ABSENT ON NULL) leaves a null key out.
  reviews: {},
  meta: { author: { id: 1, name: 'An author' } },
}

// @ts-expect-error A tuple has exactly its positions.
export const shortPair: ListBooksRow = { ...book, pair: [1] }

// @ts-expect-error Its positions have their own types.
export const swappedPair: ListBooksRow = { ...book, pair: ['1', 12.5] }

// @ts-expect-error NULL ON NULL keeps the position, so it is never absent.
export const shortSlots: ListBooksRow = { ...book, slots: ['A title', null] }

// json_objectagg's keys are data: any string, each to the value's type (here nullable: NULL ON NULL is its default).
export const tagged: ListBooksRow = { ...book, tags: { classic: 100, unweighted: null } }

// @ts-expect-error A record's values have the value's type.
export const badTag: ListBooksRow = { ...book, tags: { classic: 'many' } }

// json_object keeps a null key by default (NULL ON NULL): bio is there, and may be null.
export const author: GetAuthorAuthor = { id: 1, name: 'A', bio: null, profile: { anything: true }, books: null, reviews: null }

// @ts-expect-error bio is never absent.
export const noBio: GetAuthorAuthor = { id: 1, name: 'A', profile: null, books: null, reviews: null }

// Aggregates under GROUP BY are never null.
export const stats: AuthorStatsRow = { authorId: 1, bookIds: [1], prices: { 'A title': 1.5 } }

// @ts-expect-error ...and never null.
export const noIds: AuthorStatsRow = { ...stats, bookIds: null }

export const reviews: ReviewsByBookRow = { bookId: 1, reviews: [{ id: 1, stars: 5 }], comments: { '1': null } }
