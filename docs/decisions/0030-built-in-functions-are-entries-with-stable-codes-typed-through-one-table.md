# Built-in functions are entries with stable codes, typed through one table

**Date:** 13 Nov 2026

**Decision.** The built-in functions haruspex reads are a catalogue in Semantics, `BuiltInFunctions`, one entry per function
and form, each with a stable code (`window.count`). How inference types each is a table in Inference, one row per entry:
arity, other forms, the rule in words, the reasons to be NULL it adds, the observation tests it rests on, whether it reads only
its arguments, and its typing method. A call is typed through its row and nowhere else; window functions' rows hold their
rule as data. A test holds every entry to a row, every citation to a test that calls the function, every reason the sweep finds
to the row's declared ones, and every read of a function's name to the catalogue.

**Context.** Functions were dispatched two ways (names in the expression switch, and a set of names with a switch of its
own), and named again in sets of aggregates, of functions that read only their arguments and of JSON constructors, in CASE
folding, in the window and numeric switches, in the CHECK reader and in the binder. A function's rule had no one place, and
explaining a value had nothing stable to cite.

**Alternatives.**
- *The catalogue and the typing in one place*, each entry holding its typing method. The entry would then live in Inference,
  and the CHECK reader and the binder, in Semantics, couldn't use it. The split matches the types': Semantics says which
  functions and types there are, Inference what it makes of them.
- *Codes as numbers* (`F0042`), as some compilers' diagnostics are. A number says nothing, and needs a table to look up; the form
  and name say what the code is, and SQL Server doesn't rename its functions.
- *Every function SQL Server has.* Listing hundreds of functions nothing reads, each a claim to observe, belongs with the
  construct sweep that decides which to model; the catalogue lists what is read, and so what is not.

**Reason.** One path makes a function's rule one row, which a test can hold to the server and an explanation can cite by a code
that lasts.

**See also:** [the guide, "The SQL Server type system, as data", "Functions"](../internals/08-the-sql-server-type-system-as-data.md#functions);
[decision 0029](0029-built-in-types-are-one-catalogue-observed-row-by-row.md).
