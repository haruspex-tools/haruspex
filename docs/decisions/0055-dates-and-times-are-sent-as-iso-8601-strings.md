# Dates and times are sent as ISO 8601 strings

**Date:** 16 Nov 2026

**Decision.** A date or time parameter accepts an ISO 8601 string of exactly the form its type holds, and the client sends it
as text, for SQL Server to convert. Each type has its form:
- `date`: `2026-11-16`;
- `time(n)`: `08:30:00`, with at most `n` digits of a second;
- `datetime2(n)`: `2026-11-16T08:30:00`, with at most `n` digits of a second and no offset;
- `datetimeoffset(n)`: the same with an offset, `+05:30` or `Z`, which it must have;
- `datetime`: the form of `datetime2`, with a fraction it holds exactly (`.5`, `.12`, `.123`, `.997`: thousandths ending 0, 3
  or 7), from `1753-01-01`;
- `smalldatetime`: whole minutes (`2026-11-16T08:30:00`), from `1900-01-01` to `2079-06-06T23:59:00`.

A JavaScript `Date` is not accepted. The rules are read from the type the IR keeps for the parameter (`sql`), since the IR has no
facet for a date's range or a second's fraction.

**Context.** Observed on 16 Nov 2026 with mssql 12.7.14 (tedious 20.3.9) on SQL Server 2025 RTM-CU10
(`ParameterObservationTests`):
- the driver's own date types read a string as a JavaScript `Date`, in the process's time zone, and send it in UTC (its
  `useUTC` default): `2026-11-16T10:20:30` arrived as `15:20:30` in New York. With `useUTC` off, a `date` of `2026-11-16` became
  `2026-11-15`. `datetime2` through the driver keeps only milliseconds. So what is stored depends on where the application
  runs, the trap the design names for dates on the way out too;
- an ISO 8601 string sent as text converts exactly to each type, in every session's language, with two exceptions: a
  `datetime` or `smalldatetime` reads `2026-02-03` without a time in the session's date order (it became 2 March under
  `SET LANGUAGE british`), and the form with `T` and seconds is read the same everywhere;
- a finer fraction than the type holds is rounded without an error (`datetime` to three-hundredths of a second, and
  `29.999` seconds to the next minute in a `smalldatetime`); an offset sent to `datetime2` is dropped, and a
  `datetimeoffset` sent none takes `+00:00`, each without an error;
- a value outside the type's range is refused (error 8114).

**Alternatives.**
- *Accept `Date`.* Its value is an instant, and the types other than `datetimeoffset` hold a clock time with no zone, so some
  zone must be chosen to turn one into the other. The driver chooses by a pool setting the generated function can't see,
  and the application can't tell from the type which one it got.
- *Temporal's types (a plain date, a plain date and time, a zoned date and time).* The right types, but not in Node yet
  without a flag or a polyfill, and the design asks for no runtime but Zod. They can be accepted later beside the strings,
  since their `toString()` is this ISO form.
- *Accept any ISO string the server converts and let it round.* A value changed without an error is what the parameter checks
  exist to prevent ([decision 0054](0054-a-parameter-is-checked-against-its-declared-type.md)).
- *Read the type's fraction and range from new facets in the IR.* They would serve no other emitter yet, and the type is
  already in the IR, spelt one way ([decision 0041](0041-a-type-is-spelt-with-its-defaults-written-out.md)). A type the
  emitter doesn't know gets the ISO format alone, and a note that a finer fraction may be rounded.

**Reason.** A string says exactly the value it means, a team already has ISO strings from JSON and from `FOR JSON` results
(which the generated schemas read as strings, never as `Date`), and the server converts it the same wherever the application
runs.

**In the code:** `DateTimes` in the Zod emitter.
