# Built-in types and conversions are one catalogue of data, observed row by row

**Date:** 12 Nov 2026

**Decision.** SQL Server's built-in types are one closed catalogue, `BuiltInType`, in Semantics: every built-in type a value
can have, modelled or not, each a row of facts (family, place in data type precedence, arguments and their defaults, fixed
digits, range, fractional seconds, longest text). A type with its arguments is a `ScalarType` over it, read by Semantics and
Inference alike, and a type's name is compared as a string only in the catalogue's parser, `BuiltInType.Named`. Which
conversions SQL Server allows is a second table, `ConversionChart`, and which of them keep every value, for the types
inference models, a third, `Conversions.Table`. A test reads every row of all three back from a real server, and a sweep runs
every type through the operators that type a value, checked against the server, so a row no fixture uses is held as firmly
as one every fixture does. What the server refuses when a procedure is created is unknown.

**Context.** The facts were code, written out where each was needed: families in `SchemaRules.TypeFacts`, in
`CheckReader`, in the type's predicates, in the IR mapping and in the analyser's literal names; integer ranks, precedence and
digits as `switch` expressions; whether a conversion keeps its values as an 80-line method. A slip in one copy for one type
(an aggregate's digits) reached no fixture and was found only in a private corpus.

**Alternatives.**
- *An enum of type names, with a `switch` per attribute.* It names the types but puts their facts in as many places as there
  are attributes, which is how the copies arose.
- *Read the types and casts from a server's catalogue.* The tool never connects to a database
  ([decision 0001](0001-infer-from-source-never-a-live-database.md)); the facts belong to the version of SQL Server, not to a
  database, so they are code, and the tests observe them.
- *Transcribe Microsoft's pages.* They are the citation, but not the authority: the published conversion chart differs from
  the server for `timestamp` and `json`. Every row is read from the server, and the test fails where they part.
- *Keep the catalogue in Model*, beside `SqlType`. Model says what a declaration wrote; what a type means (its precedence, its
  range) is interpretation, which is Semantics' work, and Semantics is the lowest layer that needs it.

**Reason.** A fact written once is wrong in one place, and a table can be tested whole: read back from the server row by row,
and listed, so that what is and isn't modelled can be generated rather than kept by hand. Rules become small interpreters over
the rows.

**See also:** [the guide, "The SQL Server type system, as data"](../internals/08-the-sql-server-type-system-as-data.md);
[decision 0007](0007-every-engine-rule-is-observed.md), on observing every rule.
