# A masked column claims only its type, nullability and greatest length

**Date:** 24 Nov 2026

**Decision.** Dynamic data masking is modelled. A column declared `MASKED WITH (FUNCTION = '…')` is masked, in a folder of
scripts, a project or a package, and so is one an `ALTER TABLE … ALTER COLUMN … ADD MASKED` that may run masks, wherever it is;
a `DROP MASKED` is followed only when it certainly runs last, in the table's own script (`MaskChange`). By default haruspex assumes
the callers don't hold `UNMASK`, so of a masked column, and of every value computed from one, it claims only the type, whether it
may be null and the greatest length. A CHECK's range and set of values, a `char`'s, `nchar`'s or `binary`'s fixed length and a
minimum length are dropped; the type's own range and digits stay. JSON computed from a masked column is unknown, since its mask
is not JSON, and a top-level `FOR JSON` that nests such JSON leaves the procedure's results unknown. The config file's
`"session": { "unmask": true }` says the callers hold `UNMASK`, and every claim comes back, unless a module of the project runs
`WITH EXECUTE AS` another user (HAR2103). The IR records the declaration in `source.session`, as it records every option there,
so the IR's format doesn't change.

What the server does, observed in `MaskingObservationTests` as a user who holds only `SELECT` and `EXECUTE`:

- **Each mask is of the column's type and no longer than it.** `default()` gives `0`, `'xxxx'` (cut to the type's length),
  `0x30`, `1900-01-01`, `false`, the zero GUID, `<masked />` for xml and `{"masked":true}` for json; `email()` gives
  `aXXX@XXXX.com`, `partial()` its prefix, padding and suffix, `random(a, b)` a value between them, `datetime("Y")` the date with
  its year 2000 and `datetime("M")` with its month January. None is padded to a `char(n)` or `binary(n)`, and an empty string
  becomes four characters, so neither a fixed length nor a minimum length holds. A `random()` range the type can't hold is refused
  when the column is declared (16005), so the type's range does.
- **A mask keeps NULL and makes none.**
- **An expression whose value comes from a masked column is masked as a whole**: `rating + 1` is `0`, `'[' + v + ']'` is
  `'xxx'`, `COUNT(rating)` is `0`, a `CASE` that may give one gives `0` in its other branches too, and so does every row of a set
  operation one of whose queries gives one. A condition doesn't mask: `CASE WHEN rating = 4 THEN 'a' ELSE 'b' END` gives `'a'` or
  `'b'`. JSON built from one (`JSON_OBJECT`, a `FOR JSON` subquery) is the text `xxxx`, and a top-level `FOR JSON` writes it into
  its text without quotes, which is not JSON.
- **A rowset function given a masked argument returns every column as its mask**: `STRING_SPLIT`'s ordinal is `0`, and so is a
  multi-statement function's literal; an inline function, which is inlined, keeps its literal. `json_query` of a masked column
  in a top-level `FOR JSON` is written as its mask, unquoted.
- **A scalar function that reads a masked column returns its mask**, even with inlining off and through a variable. A value a
  procedure keeps in a variable or a temporary table is the mask, and what is computed from it later is computed as from any
  value. A view, a derived table and a procedure's caller pass a mask on.
- **A masked column of a temporary table is not masked to the session that made it**, which owns it.
- **A user who holds `UNMASK` reads every value**, and a module declared `WITH EXECUTE AS` reads with the permissions of the user
  it runs as, whatever the caller holds.

**How.** A value carries the masked column it comes from (`Value.MaskedBy`), a fact of its own, kept by every `with` and joined
as either side's; `Value.Masked` leaves it the facets a mask keeps. Every expression the typer types counts the masked values its
operands gave (`MaskTally`), and is masked when any did, so a rule that types an expression doesn't have to pass the fact on.
Conditions are read as proofs, not typed as values, so a column one reads doesn't mask what it chooses. A scalar function call is
masked when its body may read a masked column, found by name (`MaskedReaders`), and every column of a built-in or multi-statement
rowset function's call when one of its arguments is masked.

**Where it claims less than the server.** A value kept in a variable or a body's table stays masked, though the server computes
on it as on any value; a function that only filters on a masked column, and a body that names a table with one without reading
it, are taken to read it; a masked value of unknown shape in a top-level `FOR JSON` is taken to be JSON, so the results are
unknown, and so is a json column's own mask there, though it is JSON; and one module anywhere that runs as another user keeps
every mask, whichever procedures reach it.

**Context.** Audit finding F012: nothing read `MASKED WITH`, so a masked column kept its CHECK facets and fixed length, and a
caller without `UNMASK`, the reason to mask a column, received `0` where the generated schema said 1 to 5. The guide and the
package reader said permissions can't change what a procedure returns. The maintainer, 24 Nov 2026, chose to model masking, with a
setting that restores the claims, over listing masking as an assumption of the guarantee.

**Alternatives.**
- *List masking as an assumption, and claim as if callers hold `UNMASK`.* Then the default is false for every caller a mask is
  meant for, which the guarantee exists to rule out.
- *Make a masked column unknown.* Its type, whether it may be null and its greatest length hold under every mask, and an unknown
  throws them away.
- *Mask only what each rule says.* Every rule that builds a value (a `CAST`'s fixed length, a `CASE`'s literals, a set
  operation's values) would have to pass the fact on, and one that forgot would claim what the mask breaks. Counting masked
  operands per expression can't forget one.
- *Mask a value kept in a variable or a temporary table only where it was kept.* It would claim more where the server computes on
  the kept mask, but every write would have to drop the fact, and a value written in one place and read in another would follow
  the flow of the procedure, not of one expression.
- *Record the setting outside `source.session`, or the masked columns in the IR.* `source.session` is already where the IR says
  what the callers are declared to have, and it is an open map; a masked column shows in the IR as what it claims.
