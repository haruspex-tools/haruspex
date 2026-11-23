// Evidence that Zod's multipleOf can check a decimal's scale: haruspex emits decimal(p, s) as .multipleOf(1e-s).
// A JSON number is parsed to the nearest double, and neither 0.07 nor 0.01 is a double, so a naive remainder
// test would reject true multiples. Zod 4.6.7 tests the ratio value / step against the nearest integer with a
// relative tolerance of 4 epsilon (v4/core/util.js, floatSafeRemainder). Parsing the value and the step and then
// dividing each round by at most half an ulp, so a true multiple's ratio is within about 1.5 epsilon of an
// integer: inside the tolerance. This script checks that by sweeping, and fails (exit 1) on any false failure.
//
// It also checks the other direction on small values, so the check is not vacuous: a value with one decimal
// place too many is rejected. (Past about 2^50 steps the tolerance exceeds half a step and anything is accepted,
// which is weaker, never wrong.)
//
// The driver builds a rowset's decimal by its own arithmetic rather than JSON.parse's, which differs only past scale 22; the
// sweep below those covers it.
//
// Run by EmitCheckTests; takes a few seconds. Deterministic: the random sweep uses a fixed seed.
import { z } from 'zod'

let checked = 0
const failures = []

function accept(scale, text) {
  checked++
  // The literal haruspex emits, and the value as JSON.parse reads SQL Server's text.
  const step = Number(`1e-${scale}`)
  if (!z.number().multipleOf(scale === 0 ? 1 : step).safeParse(JSON.parse(text)).success && failures.length < 20)
    failures.push(`scale ${scale}: ${text} was rejected`)
}

function reject(scale, text) {
  checked++
  if (z.number().multipleOf(scale === 0 ? 1 : Number(`1e-${scale}`)).safeParse(JSON.parse(text)).success && failures.length < 20)
    failures.push(`scale ${scale}: ${text} was accepted, but has too many decimal places`)
}

const decimal = (negative, integer, fraction, scale) =>
  (negative ? '-' : '') + integer + (scale > 0 ? '.' + String(fraction).padStart(scale, '0') : '')

// The values named in the decision, and SQL Server's own edges.
for (const text of ['0.07', '1.1', '123.45', '0.29', '1.15', '4.35', '-0.07', '0.01', '99999999999999999.99', '-99999999999999999.99', '21474836.47'])
  accept(2, text)
for (const text of ['922337203685477.5807', '-922337203685477.5808', '214748.3647', '-214748.3648', '0.0001', '1.1'])
  accept(4, text)
for (const text of ['0.99999', '-0.99999', '0.00001']) accept(5, text)
accept(0, '99999999999999999999999999999999999999')
accept(38, '0.' + '9'.repeat(38))

// Every value with up to 6 decimal places and an integer part below 200, sampled evenly at the larger scales.
for (let scale = 0; scale <= 6; scale++) {
  const steps = 10 ** scale
  const stride = Math.max(1, Math.floor(steps / 500))
  for (let integer = 0; integer < 200; integer++)
    for (let fraction = 0; fraction < steps; fraction += stride) {
      accept(scale, decimal(false, integer, fraction, scale))
      accept(scale, decimal(true, integer, fraction, scale))
    }
}

// Random decimal(p, s) values for every precision up to 38 and every scale up to it.
let seed = 42
const random = () => (seed = (seed * 1103515245 + 12345) % 2147483648) / 2147483648
for (let i = 0; i < 300000; i++) {
  const precision = 1 + Math.floor(random() * 38)
  const scale = Math.floor(random() * (precision + 1))
  let digits = ''
  for (let d = 0; d < precision; d++) digits += Math.floor(random() * 10)
  const integer = digits.slice(0, precision - scale).replace(/^0+(?=\d)/, '') || '0'
  accept(scale, (random() < 0.5 ? '-' : '') + integer + (scale > 0 ? '.' + digits.slice(precision - scale) : ''))
}

// A rowset's decimal as the mssql driver builds it (tedious 20.3.9, value-parser.js readNumeric): its unscaled integer, exact
// below 2^53, divided by Math.pow(10, scale). Up to scale 22 both are exact doubles, so the quotient is the double JSON.parse
// gives and the sweeps above cover it. Past 22, 10^scale is not a double, so the driver's value can differ from JSON.parse's by
// a rounding; these are the values where the check still bites, small unscaled integers at every such scale.
for (let scale = 23; scale <= 38; scale++)
  for (let unscaled = 1; unscaled <= 20000; unscaled++) {
    checked++
    const value = unscaled / Math.pow(10, scale)
    if (!z.number().multipleOf(Number(`1e-${scale}`)).safeParse(value).success && failures.length < 20)
      failures.push(`scale ${scale}: ${unscaled} as the driver divides it was rejected`)
  }

// Not vacuous: one decimal place too many is rejected.
for (let scale = 0; scale <= 6; scale++)
  for (let integer = 0; integer < 100; integer++) reject(scale, decimal(false, integer, 1, scale + 1))

console.log(`multipleOf: ${checked} values checked, ${failures.length} wrong`)
for (const failure of failures) console.log(failure)
process.exit(failures.length === 0 ? 0 : 1)
