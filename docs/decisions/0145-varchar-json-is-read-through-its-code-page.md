# JSON in `varchar` text is read through its code page

**Date:** 28 Nov 2026.

**Decision.** JSON declared for a `varchar` or `char` column or parameter is read as its code page gives it back, by one rule
(`CodePageText`) that the column's reads, the parameter's reads and `OPENJSON … WITH`'s `varchar` columns share. A string of the
declared JSON keeps its value set only when the code page holds every character of every value; otherwise it keeps its least
length, and a greatest length of n code points becomes 2n. A key the code page doesn't hold may be missing, the object holding
it may hold a key it doesn't list, and a key it lists of the same length may hold either value. Under a `_UTF8` collation
everything declared stands. The code page is
the column's collation's, or the database's for a column without `COLLATE` and for a parameter; where neither is known, ASCII is
the floor. What a caller sends keeps all the schema says.

The generated function sends a `char` or `varchar` parameter's declared JSON with every UTF-16 code unit past U+007F written as a
JSON escape, so an emoji is its two surrogates' escapes, and checks the escaped text against the parameter's type (the
maintainer's choice, 28 Nov 2026: "I think escaping is better"). Every collation's parameter is escaped, `_UTF8` among them: it
does no harm there, and the collation isn't always known.

**Context.** `varchar` text is in its collation's code page, and a character the code page lacks, written as itself, comes back
as another: a best fit (`Ω` as `O` under 1252, `é` as `e` under 1251) or `?`, and a character outside the Basic Multilingual
Plane as `??`. A JSON escape (`\u03a9`) is ASCII, so it is kept, and the JSON functions give the character back. A declared
schema is a claim about what was written, and a writer may use either form, so a value set or length of the schema is a claim
about text the server may have changed. Decision 0143 made the parameter's reads keep a value set only when it was ASCII; a
declared `varchar` column still claimed `z.enum(["Ω", "alpha"])` for a value that arrives as `O`.

Observed (`CodePageObservationTests`, SQL Server 2025 RTM-CU10), for a collation of each of the 16 code pages SQL Server has and
one of UTF-8, over every character of the Basic Multilingual Plane but the surrogates: a character comes back unchanged exactly
when .NET's encoding of the code page, with no fallback, round-trips it; whatever it comes back as is one UTF-16 unit; a pair
outside the plane comes back as two units, and a lone surrogate as one (U+FFFD under UTF-8). So the number of UTF-16 units never
changes, each is then a character of the plane, and a string of n code points, at most 2n units, comes back as at most 2n code
points. Over a column, a parameter and an `OPENJSON … WITH` column of `varchar`, the escapes are kept and the characters written
as themselves are not, the same for `char(n)`; a column without `COLLATE`, a parameter and an `OPENJSON … WITH` column take the
database's collation. A key folds as a value does (observed by the slice's reviewer): `{"Ω":"x","O":1}` in a 1252 column is
`{"O":"x","O":1}`, so a path to `Ω` finds nothing, a path to `O` the first of the two, and `JSON.parse` the last. A path never finds
a key written as an escape either, so a key the code page lacks is never found by a path, whichever form was written.

Through the generated function and the driver (`MssqlFunctionTests`), `ā😀` sent escaped into a `varchar(60)` parameter of a 1252
database reads back as `ā😀`, where sent as itself `ā` arrives as `?`.

**Alternatives.**
- *Refuse a `varchar` parameter's value that holds a character past ASCII*, or one the declared collation's code page lacks. It
  loses values that escaping keeps, and the collation isn't always known.
- *ASCII only, whatever the collation*, as decision 0143 had it. Sound, but it drops `café` under 1252 and `Ж` under 1251, which
  the code page holds. The observation shows .NET's tables agree with the server for every character, so the code page can decide.
- *Drop every length of a string whose values aren't held*, as decision 0143 had it. A greatest length of 2n is true, and without
  any bound `JSON_VALUE` can't know a value fits in 4,000 characters, so it would lose the least length too.
- *Tables of each code page's characters in the repository.* The .NET encodings are part of the runtime, and the observation
  checks them against the server.
- *Widen `OPENJSON … WITH`'s `varchar` columns too.* Into `varchar(n)`, the length is in bytes, and a character another code page
  holds may take more of them in this one and be cut. They keep the ASCII floor, through the same rule.

**Reason.** A declaration is a claim about what was written; what the reads claim must still hold after the server has stored it.

**What it costs.** A `varchar` column's enum of characters its code page lacks is read as a string, so the generated types say
less of it, though a writer that escapes them keeps them. A greatest length read through a code page is twice the declared. An
escaped character takes six characters of a `varchar(n)` parameter's text, or twelve for one outside the Basic Multilingual Plane,
so a value fits fewer of them; the check refuses it before it is sent, rather than letting the server cut it short.

**In the code:** `CodePageText` in the inference, used by `DeclaredColumns`, `DeclaredParameters`, `Typer.Converted` and
`JsonLiterals`; `ParameterLowering.Sent` and `ZodExpression.JsonSent` in the Zod emitter; `CodePageTextTests`,
`CodePageReadTests`, `ZodParametersTests`, `CodePageObservationTests` and `MssqlFunctionTests`.
