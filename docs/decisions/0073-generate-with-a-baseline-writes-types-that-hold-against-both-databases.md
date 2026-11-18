# `generate` with a baseline writes types that hold against both databases

**Date:** 18 Nov 2026

**Decision.** `haruspex generate` takes a baseline, as `check` does (`--baseline`, or `"baseline"` in the config file), the database
the application meets before the change is deployed. It combines the source's document with the baseline's
(`IrCombination`), and every emitter writes the combined one, so the application works against the database before and after.
Values flow two ways, and each is combined in its own direction, as
[decision 0068](0068-a-baseline-fits-when-the-running-application-accepts-every-value.md) judges them:

- **Results widen.** Each takes the widest type both databases allow, merged into one object type rather than given as
  alternatives: a key only one returns may be left out; a value is nullable if either's is; a length, a range, digits and a value
  set are the larger; integers of two sizes the wider; a trailing result set only one returns may be missing (`not-in-both`, so its
  function doesn't count on it); a value of another kind is a union of both; a fact only one side has is dropped. A key that went
  with another that appeared in the same type is a warning, a possible rename (HAR6034): the combined type has both.
- **Parameters narrow.** The function sends only what both procedures take: a parameter only one has is left out while it has a
  default there; a type is the narrower of the two (a shorter length, a smaller integer, a tighter range, the common values, and
  for two decimals neither of which holds the other, the fewer places on each side of the point: `decimal(9,3)` and
  `decimal(9,2)` meet at `decimal(8,2)`); a parameter has a default only where both have one, and is `OUTPUT` only where both are.
  An `OUTPUT` parameter in both is declared once, and its value both goes and comes back, so it is bridged only when its type is
  the same in both: declared as the narrower, the wider value returned fails the call (8114) or a longer string is cut short.
- **What can't be bridged in one call** is an error at its procedure (HAR6033), which the module leaves out while it is written for
  every other: a new required parameter, a removed one the baseline requires, types with no value in common (or lengths counted in
  another unit, characters against bytes), an `OUTPUT` parameter of another type, a result set read another way (rows against
  JSON), a query file whose text changed since an IR baseline, and a query file whose text names what the baseline's database
  doesn't have (a table, a column or a procedure its reading there couldn't bind, which fails there with 207, 208 or 2812, though
  the unknown it gives would accept any value). Each is said with the SQL types it names. The run fails, and so does `check`.
- **Named result sets are read from a source baseline by position.** The baseline is read with the module's config file, whose
  `sets` name the source's sets; where the baseline returns fewer, they take the list's first names, and where it returns more,
  none (`HaruspexConfig.AppliedToBaseline`), rather than stopping the run. A trailing set only the baseline returns, with no name of
  its own, is then set aside, as the function sets aside any set after those it knows, and is unbridgeable under
  `"objects": "strict"`, where the function refuses it.
- **A procedure only one database has** is left out with a warning (HAR6035): one only the source has can't be called before the
  change is deployed, and one only the baseline has can't be called after.
- **A function whose return shape changes** under [decision 0060](0060-a-call-returns-its-one-result-itself.md), one result becoming
  several, is a warning (HAR6028), as for `check`.
- **The combined document is held to the fit itself**: an application built against it must meet each database with nothing
  refused. Anything refused is reported as unbridgeable and its procedure left out, so it never claims a fit that can't be shown.
  A parameter unknown in both stays unknown and claims nothing, as in a document of one database.
- **`check` compares the output folder with what `generate` with the same arguments writes**, so with a baseline, with the combined
  module; the fit with the baseline judges the source's own document. `check --fit-only` combines nothing.

**Context.** An application and its database deploy separately. [Decision 0069](0069-check-with-a-baseline-judges-the-deploy-window.md)
gave the database's side, which refuses a change the running application would fail on; this is the application's side, a
release that works against the database before and after its change. Richard settled the design on 18 Nov 2026: merged types by
default, a change that can't be bridged an error naming the procedure, the module still written for the rest.

**Alternatives.**
- *Alternatives (`Old | New`).* Exact, but awkward to narrow in TypeScript; possibly later, as `"baselineTypes": "alternatives"`.
- *Write the source's types and warn.* The function then fails at run time against the old database, which is what this exists to
  prevent.
- *Fail the whole run on one unbridgeable change.* One stuck procedure would hold up every other.
- *Treat a return shape that flips as unbridgeable.* It is bridged: the combined function, with the trailing set
  that only one database returns as one that may be missing, works against both. What changes is the application's code, which
  is rebuilt anyway, as for any change to the module; the warning says so, with the advice to name the sets
  ([decision 0070](0070-a-procedure-s-result-sets-are-named-all-or-none.md)), and the design settled it as a warning.
- *Keep a procedure only one database has, with a warning.* Its call fails against the other database at run time; leaving it out
  makes that a compile error, which is caught before the deploy.
- *Combine nothing in `check` unless `generate` did*: two runs given the same baseline would disagree about what the module is.

**Reason.** Soundness holds for the set of two databases: every value either returns is accepted, and every call is one both take.
The fit operation decision 0068 built is the judge of both halves, so the two can't drift apart. Holding the combined document to
it also changed the fit in two places: a trailing set that the application's function doesn't count on, and may read as missing,
is ignored when it goes, not refused; and an `OUTPUT` parameter's value, which comes back, is judged as a result as well as a
parameter, so one widened is refused.

**In the code:** `IrCombination`, `IrCombined`, `IrCombinationFinding`; `CombinedTypes`, `Baseline` and `ModuleRun` in the CLI;
`IrCombinationTests`, `GenerateBaselineTests` and, on the server, `CombinedTypesTests`; [HAR6033](../diagnostics/HAR6033.md) to
[HAR6036](../diagnostics/HAR6036.md).
