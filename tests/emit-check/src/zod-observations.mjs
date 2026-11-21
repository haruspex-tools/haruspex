// What the pinned Zod accepts, for the choices the emitters and the config reader make on it (ZodObservationTests): z.json(),
// which any JSON is written as; and the ISO checks of dates and times, against the forms RFC 3339, which JSON Schema's formats
// name, allows. Prints one JSON object of the results.
import { z } from 'zod'

const json = z.json()
const values = { number: 1, string: 'a', null: null, boolean: true, object: { a: [1, { b: null }] }, array: [1, 2], undefined, function: () => 1, nan: NaN, infinity: Infinity, date: new Date(0), bigint: 10n }
const iso = {
  'time 09:00:00': z.iso.time().safeParse('09:00:00').success,
  'time 09:00:00Z': z.iso.time().safeParse('09:00:00Z').success,
  'time 09:00:00+01:00': z.iso.time().safeParse('09:00:00+01:00').success,
  'date-time 2026-11-21T09:00:00Z': z.iso.datetime({ offset: true }).safeParse('2026-11-21T09:00:00Z').success,
  'date-time 2026-11-21t09:00:00z': z.iso.datetime({ offset: true }).safeParse('2026-11-21t09:00:00z').success,
  'date-time 2026-12-31T23:59:60Z': z.iso.datetime({ offset: true }).safeParse('2026-12-31T23:59:60Z').success,
  'date 2026-11-21': z.iso.date().safeParse('2026-11-21').success,
}

// A string's length, which JSON Schema counts in code points: a character outside the Basic Multilingual Plane is one, though
// two UTF-16 code units, and so is a lone surrogate, written as an escape since JavaScript's source can't hold one alone.
const astral = '\u{1F600}\u{1F600}'
const lone = JSON.parse('"\\ud800a"')
const lengths = {
  'astral units': astral.length,
  'astral max 2': z.string().max(2).safeParse(astral).success,
  'astral max 1': z.string().max(1).safeParse(astral).success,
  'astral min 2': z.string().min(2).safeParse(astral).success,
  'astral min 3': z.string().min(3).safeParse(astral).success,
  'lone units': lone.length,
  'lone max 2': z.string().max(2).safeParse(lone).success,
  'lone max 1': z.string().max(1).safeParse(lone).success,
  'lone min 2': z.string().min(2).safeParse(lone).success,
  'lone min 3': z.string().min(3).safeParse(lone).success,
}

process.stdout.write(JSON.stringify({
  lengths,
  json: Object.fromEntries(Object.entries(values).map(([k, v]) => [k, json.safeParse(v).success])),
  iso,
}) + '\n')
