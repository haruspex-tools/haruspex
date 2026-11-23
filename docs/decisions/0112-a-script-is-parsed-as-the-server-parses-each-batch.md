# A script is parsed as the server parses each batch, and its synonyms are played in order

**Date:** 23 Nov 2026

**Decision.** A folder's or a project's script is parsed batch by batch, each batch with the `QUOTED_IDENTIFIER` the server
has where the batch starts (`ScriptParser`), so `"x"` after a `SET QUOTED_IDENTIFIER OFF` is the string it is there, not a
name. A synonym is read as a procedure is ([decision 0080](0080-the-last-definition-in-a-script-stands.md)): within a script
the last `CREATE SYNONYM` that runs stands, after a `DROP SYNONYM`, `DROP SYNONYM IF EXISTS` or a drop under `IF OBJECT_ID(…)
IS NOT NULL` naming it.

**What the server does**, observed (`ExecObservationTests.Definitions.cs`). `SET QUOTED_IDENTIFIER` takes effect where it is
parsed, not where it runs: from there on in its own batch and in every batch after, under an `IF` that is false too.
`SET ANSI_DEFAULTS` sets it as well. In a procedure's or trigger's body it changes nothing: the body is parsed with the
setting in force where it is created, and the session keeps its own. `ALTER DATABASE … SET QUOTED_IDENTIFIER OFF` changes only
the database's default, which a connection that sets the option, as sqlcmd does, overrides (observed with sqlcmd). A synonym dropped and created again stands for its last
target, and a procedure that reads through it reads that target's columns.

**What ScriptDom does.** It follows `SET QUOTED_IDENTIFIER` to the end of its batch, but starts each batch with the setting it
was given, doesn't follow `SET ANSI_DEFAULTS`, and follows a `SET` in a body as if it were outside. So haruspex works out from
the tokens where each batch starts and the setting there, and parses each run of batches that start alike with that setting,
from the script with everything outside the run turned into spaces, so that every location is the script's own. Where
ScriptDom would still read a `"…"` otherwise than the server does (after a `SET ANSI_DEFAULTS` in the same batch, or after a
`SET` in a body), the script isn't read, with [HAR1001](../diagnostics/HAR1001.md) at the `"…"`.

**A project.** A project's build refuses a `SET` in a script it builds (SQL70001, a statement it doesn't accept there, observed with
`Microsoft.Build.Sql` 2.3.0), so a project that holds one has no package to compare with. Its scripts
are read as a folder's are, the `SET` followed as the server follows it when the scripts are run by hand, which is what
`ANSI_NULLS` already did.

**Synonyms across scripts, and a doubt.** Two scripts that each create a synonym of one name are
[HAR1002](../diagnostics/HAR1002.md), a drop before the second or not: which runs last a folder doesn't say. A `CREATE` after a
drop that may not run is HAR1002 too, at the drop: a synonym has no `Redefinition` to record the doubt, and adding one is a
change to the model and the IR this didn't need. A `DROP SYNONYM` in a script that doesn't create the synonym is passed over:
whichever runs first, a reference through it reads the one target or fails because there is none, and never reads another.

**A synonym created where it may run.** A `CREATE SYNONYM` under `IF`, in a block, in a procedure's body or in a deployment
script moves the synonym when it runs (observed), and whether it runs isn't knowable. When every such statement names the
target the model has, or the model has none and they all name one, that target stands: if none runs, a read through the name
fails rather than reads another object. When one names another target, which stands isn't known: it is HAR1002 at that
`CREATE`, as a `CREATE` after a drop that may not run is, and the synonym is left out of the model. The pass that finds it runs
once every script is read, before a read gathers its diagnostics (`SchemaDeclarations.SettleSynonyms`).

**Context.** Audit run 1's F004 and F009. Folders of scripts SSMS generated for old procedures are where `QUOTED_IDENTIFIER` is
off and `"…"` holds strings, and the whole file was parsed with it on: a string that happened to name a column was typed as
the column. A folder of numbered migrations that moves a synonym kept its first target, the one no longer in effect, with no
diagnostic.

**Alternatives.**
- *Parse again from the `SET`, with the text before it cut off and locations shifted.* A `SET` inside a block would leave the
  rest of the parse unbalanced, and shifting every location is a second place to get them wrong. Turning what's outside the run
  into spaces keeps every line, column and offset as they are.
- *Give a diagnostic to any script that turns the option off and holds a `"…"`, and leave its modules unknown.* Sound, but it
  throws away what the server does read, which is knowable.
- *Record a synonym's doubt in the model.* Reads through it would become unknown instead of stopping the run, and the unknown
  could name the statement. Left for when it is wanted: it changes the IR.
