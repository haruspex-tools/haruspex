# An absent JSON result reads as null, as the mssql driver hands it over

**Date:** 16 Nov 2026

**Decision.** A procedure's `FOR JSON` result has one schema, of the document, which accepts exactly what the mssql driver
hands an application for it with `parseJSON` on: the parsed document, or `null` when the query matched no rows, whether the
document is an array or one object. So a JSON array that may be absent is `.nullable()`, and one that arrives has at least
one item (`.min(1)`, or `.length(1)` when the query returns at most one row). There is no setting for the driver's mode and
no second export: without `parseJSON`, the application turns the row's text into the same value in one line, which the
README shows. Rowsets are unchanged: an absent one still reads as `[]`. The IR's reader convention says the same, so the
client module and its runtime package, which will reassemble JSON too, follow one rule.

**Context.** Observed on 16 Nov 2026 with mssql 12.7.14 (tedious 20.3.9) on SQL Server 2025 RTM-CU10
(`MssqlDriverObservationTests`). The driver recognises a top-level `FOR JSON` result set by the one column SQL Server names
`JSON_F52E2B61-18A1-11d1-B105-00805F49916B`, whatever its options (`PATH`, `AUTO`, `WITHOUT_ARRAY_WRAPPER`, `ROOT`), and
joins the rows it is split over into one row, through `query` and `execute` alike:
- with `parseJSON: true`, the row is the parsed document; when no rows matched (SQL Server sends a result set with no row),
  it is `null`, for an array, an object and a `ROOT` alike. Text that doesn't parse fails the call (`EJSON`).
- without it, the row is an object holding the joined text under that column's name, and `''` when no rows matched.
- JSON anywhere else, a `FOR JSON` subquery, `json_object`, a `json` column, a `FOR JSON` beside another column, is an
  ordinary rowset holding text, in either mode. `FOR XML` is joined the same way and never parsed.
- The driver goes by the name alone, matching case: a rowset whose one column is given that name (or `FOR XML`'s) is joined
  into one row too, in either mode, and parsed with `parseJSON`. Its shape does depend on the mode, and on its rows, so the
  inference makes such a result set unknown, saying why, rather than type rows that never arrive.

Before, the README told applications to join the rows themselves and read no row as `[]` for an array, and the schema of an
array result didn't accept `null`. An application in the common mode, whose every procedure's document is `recordset[0]`,
got a parse error whenever a list was empty.

**Alternatives.**
- *A setting naming the driver's mode* (`"driver": { "parseJSON": true }`) that changes what each JSON result's schema
  describes. Once an absent result is `null` in both modes, the document's shape doesn't depend on the mode at all: the only
  difference is whether the driver or the application parses the text. A setting would change nothing, or would describe
  the text mode's row (`{ "JSON_F52E…": string }`), whose type tells the application nothing about its data.
- *Both shapes exported* for each procedure, the document and the driver's row. Twice the names for one fact, and a haruspex
  idea (which export fits which mode) in every application, against "fit in; don't impose".
- *A schema of the whole result set* (`z.tuple([document.nullable()])`), so that `recordset` itself is parsed, like a
  rowset's. Its type would be a one-item tuple, not the application's document, and it wouldn't fit the text mode.
- *Keep `[]` for an absent array.* Nothing hands an application `[]` for it: the driver hands over `null`, and so would any
  reader that doesn't know the result's type.

**Reason.** The schema accepts what arrives, in the mode applications use, with no setting and no wrapper; the other mode
is a line of the application's own code. It also claims more than before: a present `FOR JSON` array is never empty.

**In the code:** `ZodLowering.Result` and `ZodLowering.Rows`; `Typer.Rowset`, for a rowset under the driver's column
name; and the reader convention in the IR's README ("Results").
