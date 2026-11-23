# A value set is canonical by a fold observed from the server

**Date:** 14 Nov 2026

**Decision.** A CHECK constraint's set of strings is **canonical** (IR match `canonical`, 0.20) when:
- its column is `char` or `varchar` in a code page, and the column's collation (its own `COLLATE`, else the database's default
  from the DACPAC or the config file's `collation`) is one whose fold is proven;
- every string is plain (ASCII letters and digits, space, `_`, `-`, `.`);
- no two strings fold alike.

A fold is data observed from the server, not Unicode rules. For each proven collation it lists the characters of the code page
the collation ignores, and the characters equal to each class of plain characters (`a` and `A`, `æ` and `AE`). A string's
canonical form drops the ignored characters, puts each class's name for its characters, keeps the rest, and then drops trailing
spaces. The IR carries each fold a set names (`folds`), and an emitter maps a value to the item whose canonical form it has before
treating the set as exact: `z.preprocess(canonicalOf(…), z.enum([…]))`. The items are in ordinal order, since an extracted package
lists them in the server's order. Every other set keeps its comment, and parameters are unchanged.

Proven: `SQL_Latin1_General_CP1_CI_AS`, `Latin1_General_CI_AS`, `Latin1_General_100_CI_AS`, `Latin1_General_100_CS_AS` and
`Latin1_General_100_CI_AI`, for `char` and `varchar`. Not proven: `nchar`, `nvarchar` and every `_UTF8` collation.

**Context.** A set read under a collation was a comment: `'Paid '` passes `IN ('paid')` in a case-insensitive collation, so an
enum would refuse values the database stores. The roadmap asked for the values themselves, where agreement with the server can be
proven, and the comment where it can't.

**Alternatives.**
- *A fold from the collation's name* (case-insensitive: lower-case; accent-insensitive: strip marks). It accepts values the server
  wouldn't store and refuses ones it would: `æ` equals `ae`, the soft hyphen is ignored only in `_100` collations, superscript
  digits equal digits (all observed). The fold has to be the server's, so it is observed.
- *Unicode columns too.* Under the Unicode rules some characters compare by their neighbours: a repeat mark (U+3031) equals the
  character before it, so `'a'` and the mark equal `'aa'` and `'z'` and the mark `'zz'`, in every collation above, and some Indic
  and Thai signs are ignored except between two hyphens (observed: `CollationFoldGridTests`,
  `Under_the_Unicode_rules_a_repeat_mark_equals_the_character_before_it`). A fold of one character at a time can't be that
  equality, and modelling the rules behind it would be guessing.
- *A database round trip at run time.* The generated code runs where no database is.

**Reason.** The fold is the server's equality, and is proven both ways:
- `CollationFoldGridTests` derives each fold from the server, comparing every character of the code page with every plain
  character and with the empty string, beside each plain character on either side (132 contexts). It fails if a class differs
  between contexts, and it fails if the committed data file (`src/Haruspex.SqlServer.Semantics/Collations`) isn't what it observes.
- `CanonicalValueSetObservationTests` stores 606,720 strings per collation in a `varchar` column of it: every character at every
  position of sixteen members, and every pair at three positions of three. It finds each value equal to a member exactly when
  their canonical forms are equal.

A code page holds about 250 characters, so the proof covers every one. A collation not proven, or a set with a character outside
the plain ones, claims what it did before.

**Amended** (22 Nov 2026). The proof compares a `varchar` value with `varchar` strings, so it covers a set written without `N`.
An `N'…'` literal outranks `varchar`: the column is converted to Unicode and compared by the collation's Unicode rules, which
are not its code-page rules. Under `SQL_Latin1_General_CP1_CI_AS`, `'v²'` equals `N'v2'`, a NUL is ignored and `'paß'` equals
`N'pass'`, though each is refused by the same set without `N` (observed:
`A_national_literal_compares_a_code_page_column_by_unicode_rules`). So a set with any `N'…'` literal is not canonical, and keeps
its comment. A literal without `N` and outside printable ASCII is converted to the database's code page before it is compared,
whatever the column (observed: `A_literal_without_n_is_converted_to_the_databases_code_page_before_it_is_compared`), so such a set
keeps its comment under a binary collation too.

**See also:** [Semantics]
(../../src/Haruspex.SqlServer.Semantics/README.md), "What it guarantees";
[the IR README](../../src/Haruspex.Ir/README.md), "folds"; [Configuration](../configuration.md), "The database's collation".
