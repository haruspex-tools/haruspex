# A length is the server's, and a target claims the fewest code points it can hold

**Date:** 21 Nov 2026

**Decision.** The IR states a string's length in the unit SQL Server counts: UTF-16 code units for `nchar` and `nvarchar`,
bytes of the collation's encoding for `char` and `varchar`, and code points only where a declaration gives them. A target
that counts another unit translates, through one conversion beside the facts (`IrLength.FewestCodePoints`,
`IrLength.FewestUtf16Units`, `IrMinLength.FewestCodePoints`):
- a maximum of n in any unit is a maximum of n code points, since a code point takes at least one unit or byte;
- a fixed length or a minimum is a minimum only of the fewest code points a value of it can hold: n divided by the most one code
  point takes (two UTF-16 code units, four bytes of UTF-8 or of an encoding not declared, two of a double-byte code page, one of a
  single-byte one), rounded up. Where that is n itself, the length is exact.

A **stored** `nchar(n)` is exactly n code units. A value **converted** to `nchar(n)` (by `CAST` or `CONVERT`, a variable, a
parameter read in the body, `GREATEST` and the other functions whose type the server's rules compute) is at most n and at least
n - 1: under a supplementary-character (`_SC`) collation, the value's or the database's, a conversion cuts a value before a
surrogate pair that doesn't fit and doesn't pad it. So the SQL Server front end writes `maxLength` n and `minLength` n - 1 for it,
not `length` n. A procedure's own parameter keeps `length` n, which is what a client sends and binds (`NChar(n)`). A `char(n)`
conversion is cut between characters and padded, in every code page, so it stays fixed.

**Context.** Observed on 21 Nov 2026 with mssql 12.7.14 on SQL Server 2025 RTM-CU10
(`A_string_s_length_is_counted_by_the_server_in_code_units_or_bytes_and_by_zod_in_code_points`), and the pinned Zod 4.6.7
(`ZodObservationTests`): Zod's `.min`, `.max` and `.length` count code points, as JSON Schema's `minLength` and `maxLength` do.
`LEN` counts code units, or code points under `_SC`, never more than the code units. An `nchar(5)` holding two emoji is 5 code
units and 3 code points, which `.length(5)` refused; `CAST` of two emoji to `nchar(3)` under `_SC` is one emoji, 2 code units, and
of one emoji to `nchar(1)` is empty. A UTF-8 `char(8)` holding two emoji is 8 bytes, 4 code units and 2 code points.

**Alternatives.**
- *Lengths in code points in the IR.* The IR states facts about SQL Server, which counts units and bytes; a code-point length
  would be a target's view, and wrong for a target that counts UTF-16 (the parameter checks do, as the server does: decision
  0054). The conversion belongs to the reader.
- *Drop a fixed length's minimum.* Sound, but it throws away what the server guarantees: a stored `nchar(n)` holds at least half of
  n code points, and a single-byte `char(n)` exactly n.
- *Keep `length` n for a converted `nchar(n)` and widen in the emitters.* Then the IR's "exactly n" is false where SQL Server
  converts, and every reader of the IR (combination, fit, `explain`) would have to know the exception.
- *Know the collation and keep `length` n where it isn't `_SC`.* The collation of a conversion is the value's or the database's,
  which the front end doesn't always know (a database's default isn't in the source). Left for a later refinement.

**Reason.** Each layer keeps its own unit: the IR the server's, each emitter its target's, and one conversion between them that
every reader shares, so a length is read the same way everywhere.

**Amended** (22 Nov 2026, IR 0.29, with [decision 0097](0097-a-path-into-json-of-known-shape-is-walked-step-by-step.md)). A minimum
has a unit too: UTF-16 code units, or code points for a declaration's `minLength` and for a CHECK's `LEN` where `LEN` counts a
surrogate pair as one character: of `nchar` and `nvarchar` under an `_SC` collation or a version 140 one that isn't binary, and of
`char` and `varchar` under a `_UTF8` collation, binary too (observed:
`Len_counts_a_surrogate_pair_as_one_only_under_a_supplementary_character_collation`; `nvarchar` under a binary `_UTF8` collation
counts two). A minimum in code points is claimed as it is;
one in code units, as before, as half as many code points. Under an unknown collation `LEN` stays in code units, which hold under
either. Two minima in different units are compared by what each guarantees in the other's (`IrMinLength.Implies`).

**In the code:** `IrLength`, `IrMinLength`; `SqlTypes.Converted` and `Typer.Unpadded` in the SQL Server front end; the Zod
emitter's `Facets.String`, the JSON Schema emitter's `Strings`, `IrCombination` and `IrFit`; the IR README's "Facets".
