# The callers' session options are declared, and not known by default

**Date:** 14 Nov 2026

**Decision.** The config file may declare, in a `session` block, the `SET` options the callers' connections set:
`ansiWarnings`, `arithAbort` and `concatNullYieldsNull`, each `true` or `false`. An option left out is not known, and every
rule that depends on it claims only what holds whatever it is, which is what haruspex claimed before 0.17. With
`ANSI_WARNINGS` or `ARITHABORT` declared on, an overflow, a division by zero, a `COUNT` past `int` and a value outside a math
function's domain are errors, which fail the call, so the reasons `arithmetic-overflow`, `count-overflow` and `domain-error`
are not given. With `CONCAT_NULL_YIELDS_NULL` declared off, string `+` is null only when both sides may be. The IR records
what was declared (`source.session`, 0.17), and the Zod module's header says it. Options a module keeps from its creation
(`ANSI_NULLS`, `QUOTED_IDENTIFIER`) are still read from its source.

**Context.** These options belong to the session that calls a procedure, not to the procedure: a procedure created with both
off still raises the error when its caller has `ANSI_WARNINGS` on
(`A_procedure_runs_under_its_callers_options_not_those_of_whoever_created_it`). The caller is never seen, so haruspex
assumed nothing, and every expression that could overflow was nullable ([decision 0014](0014-count-is-nullable.md)). An
application's own schemas, compared with haruspex's, claimed more on many such paths, and was right on each: its driver
connects with `ANSI_WARNINGS` on. Each rule rests on observations of every combination of the two options
(`An_overflow_is_null_only_when_ansi_warnings_and_arithabort_are_both_off`,
`Count_over_more_rows_than_int_holds_is_an_error_unless_ansi_warnings_and_arithabort_are_both_off`,
`A_value_outside_a_math_functions_domain_is_null_only_when_both_are_off`): NULL only when both are off, and either one on
makes it an error. `ANSI_WARNINGS` on is enough even with `ARITHABORT` set off after it.

**Why the callers and not the database.** A database has defaults for these options (`SqlDatabaseOptions` in a DACPAC's
`model.xml`, the database settings of a `.sqlproj`, `ALTER DATABASE … SET ANSI_WARNINGS`), but they apply only to a
connection that sets nothing, and current drivers set them at login, so they would describe a session no caller has. The
declaration is about the drivers and the code that call the procedures, which the project can't see either; that is why it is
a declaration in the config file, as declared results are ([decision 0013](0013-results-that-cannot-be-inferred-are-declared.md)).

**Why not known is the default.** Assuming the defaults most drivers use would be right for most users and silently wrong for
the rest: a caller with both options off (an old ODBC data source, a job that sets them) gets NULL where the schema says a
number. The principle is to claim no more than holds, and an option left out is the only reading that never does. It is also
what keeps output byte for byte as it was for anyone who declares nothing.

**What drivers set.** For users choosing what to declare, not presets the tool assumes; a driver's version or the
application's own `SET`s may change them, so check the session (`SELECT SESSIONPROPERTY('ANSI_WARNINGS')`):

| Caller | `ANSI_WARNINGS` | `ARITHABORT` | `CONCAT_NULL_YIELDS_NULL` | Source |
|---|---|---|---|---|
| tedious (`mssql` on Node) | on | on | on | [tedious's connection options](https://tediousjs.github.io/tedious/api-connection.html): `enableAnsiWarnings`, `enableArithAbort` and `enableConcatNullYieldsNull` default to true |
| Microsoft.Data.SqlClient | on | off | on | observed: `A_sqlclient_connection_sets_these_options_at_login` (not published) |
| ODBC and OLE DB drivers | on | not published | not published | [SET ANSI_WARNINGS](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-ansi-warnings-transact-sql) says they set it on with a connection flag; [SET ARITHABORT](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-arithabort-transact-sql) says an application's connection defaults to off |
| SQL Server Management Studio | not published | on | not published | [SET ARITHABORT](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-arithabort-transact-sql) |
| JDBC (mssql-jdbc) | not published | not published | not published | none found |

`ANSI_WARNINGS` on is what matters for overflow, and every driver with a published or observed value has it.

**Options left out, and why.**
- *`NUMERIC_ROUNDABORT`* changes no claim that holds once `ANSI_WARNINGS` or `ARITHABORT` is declared on: a loss of precision
  is then an error too. With both off it makes a decimal rounded to fewer places NULL
  (`Numeric_roundabort_on_makes_a_loss_of_precision_null_when_ansi_warnings_and_arithabort_are_both_off`), which haruspex does
  not model. That is a known limitation, older than this decision: with nothing declared, haruspex's output is unsound for a
  caller with `NUMERIC_ROUNDABORT` on and both `ANSI_WARNINGS` and `ARITHABORT` off, and for no other. Closing it changes the
  output for everyone who declares nothing, so it is a slice of its own, the precision-loss rule, and `numericRoundAbort` is
  added with it, not before, so that the config file never accepts a setting nothing reads.
- *`DATEFIRST`, `LANGUAGE`, `DATEFORMAT`* change names, numbering and which strings convert, but no claim haruspex makes:
  `DATEPART(weekday, …)` is 1 to 7 whatever the first day, `DATENAME` is never a closed set, a style with month names is a
  string of the target's length, and a string that doesn't convert is an error, or NULL under `TRY_CAST`, which is nullable
  anyway (`Datefirst_and_language_change_names_and_numbering_but_no_claim_haruspex_makes`,
  `Dateformat_decides_which_strings_convert_but_a_failure_is_an_error_whatever_it_is`). A declared language could make
  `DATENAME`'s names a closed set; that would be a new claim, for a later slice.

A setting no rule reads is not added: it would look as if it changed the output when it doesn't. One written anyway is an
unknown setting (HAR3004).

**A `SET` in a body.** It overrides the caller's option until the procedure returns. `SET ANSI_WARNINGS ON` or
`SET ARITHABORT ON` there can only make an overflow an error, which claims less if not read, so it is passed over as before. Any
other value of these, and `CONCAT_NULL_YIELDS_NULL` either way, stops the walk with an unknown, as it did: a declaration never
holds past one of them.

**The IR and an older reader.** 0.17 adds `source.session`, which a file leaves out when nothing is declared. So every 0.16 file
is a 0.17 file that declares nothing and means what it meant, and the reader reads it: the one exception to
[decision 0024](0024-an-ir-reader-refuses-other-versions.md), whose reasons (a mapped file would carry an older engine's claims
under a newer label) don't hold where nothing is mapped.

**Alternatives.**
- *Assume the common drivers' options*: wrong in silence for a caller that sets them otherwise (above).
- *Read the database's options from the project*: they describe connections that set nothing, which current drivers don't
  open (above). The DACPAC reader may report them, but not as the session.
- *Presets per driver* (`"session": "tedious"`): a driver's defaults change between versions and an application may `SET`
  its own; the user knows their callers, and the table above helps them say so in three words.
- *A post-pass that removes the overflow reasons from the IR*: a reason that goes changes what other rules decide (an
  `ISNULL` whose second argument can no longer give NULL is never null), so the rules read the declaration where they make
  each reason.

**In the code:** `SessionOptions` (the declaration, and `SessionOptions.OverflowIsAnError`), read by the typer through
`Summaries.Session`; `IrSource.Session`; the config file's `session` block. The observations are
`SessionObservationTests`.
