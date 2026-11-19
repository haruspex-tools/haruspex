# The last definition in a script stands

**Date:** 19 Nov 2026

**Decision.** A script's procedures, functions and views are read in the order its batches run, and the definition that stands
is the last one that runs: a stub created and then altered is its `ALTER`; one dropped (by `DROP`, `DROP … IF EXISTS`, or a
drop under `IF OBJECT_ID(N'name') IS NOT NULL` naming it, with no type or the type of what stands) and created again is its last `CREATE`; `CREATE OR ALTER` twice is its
second. A second `CREATE` of a name that stands is refused by the server, so the first stands and the second is reported
([HAR1002](../diagnostics/HAR1002.md)), as across scripts. A function's kind (scalar, inline or multi-statement) can't change
in place: an `ALTER` or `CREATE OR ALTER` of another kind is refused, and the earlier definition stands. A drop under any other condition may not run (`OBJECT_ID` with a type finds only an object of that type, observed, so `N'FN'`
guarding the drop of an inline function never drops it), so a `CREATE` after
it leaves which definition stands in doubt, recorded as a `Redefinition`.

Across scripts nothing changes: a folder doesn't say the order its scripts run in, so a definition in one and an `ALTER` or
`DROP` in another is a doubt, and two definitions are HAR1002. **One exception:** an `ALTER PROCEDURE` (or `ALTER VIEW`) that
no other script's statement names stands, with no doubt, as the `ALTER` of a stub made where the reader can't see, by dynamic
SQL (`exec sp_executesql N'create procedure … as return 0'` under `IF OBJECT_ID(…) IS NULL`). An `ALTER FUNCTION` alone stays
a doubt: the stub it alters may be of another kind, which would refuse it, and an unseen stub's kind is unknown.

Like everything read from a script, this assumes the script deploys without error. A statement the server refuses for any
other reason (an `ALTER` whose body names a column that doesn't exist fails with 207, observed, and leaves the stub) is not
foreseen, as a `CREATE` that fails isn't: a deployment that reports errors has not deployed the project.

The earlier definitions are passed over, not modelled and not reported: the object's location is the one that stands, which
`haruspex explain` shows. Tables are not read this way (an `ALTER TABLE` that could change a column stays a doubt wherever it
is), nor are triggers, which keep every definition, since any of them may be the one that fires; both only claim less.

**Context.** Creating a stub and then altering it, or dropping and recreating, is how a deployment script installs a procedure
that may already exist. Every such script read as `redefined`: the order that decides it was ignored even where the script
fixes it. The harvest counted 16 procedures, all of them stubs made by dynamic SQL and then altered.

What the server does was observed (`ExecObservationTests.Definitions.cs`): an `ALTER` replaces the whole definition, parameters
included; an `ALTER` of a name that doesn't exist is refused (208) and leaves none; a second `CREATE` is refused (2714) and the
first stands; each `DROP` form leaves none; an `ALTER` keeps the `ANSI_NULLS` setting in force when it runs, not the stub's; a
function and a view follow the same rules, except that a function's kind can't change by `ALTER` or `CREATE OR ALTER` (2010),
and the earlier definition stands.

**Alternatives.**
- *Keep every second definition a doubt.* Sound, but it throws away what the script fixes, which "claim everything the
  engine guarantees" ([decision 0005](0005-claim-everything-the-engine-guarantees.md)) forbids.
- *An `ALTER` alone is a doubt.* Kept for a function, whose `ALTER` the unseen stub's kind may refuse. For a procedure: the stub
  it alters is unseen, but whatever stood before, the `ALTER` replaces it whole, and if nothing stood it fails and there is no
  object to call. The only thing that could make the `ALTER`'s definition wrong is a
  statement that runs after it, which is the same doubt a lone `CREATE` carries: dynamic SQL elsewhere, which the reader never
  sees. So an `ALTER` alone is as good evidence as a `CREATE` alone.
- *Report each earlier definition as a note.* Every deployment script would report its stubs, which is the idiom working as
  intended, not something for the reader to act on.
- *Apply the order across scripts too.* A folder's file order is not the order a deployment runs them in.

**Reason.** Within a script, the order is the script's own and the server's rules for each statement are observed, so the
definition that stands is known exactly; between scripts it isn't, and nothing changes there.

**Sources.** A folder and a project's scripts are read alike, so the rule holds for both. A project can't build such a script,
though, since a model has one definition per object: observed with `Microsoft.Build.Sql` 2.3.0, an `ALTER` or a `DROP` among
the scripts a build reads fails it (SQL70001, a statement the build doesn't accept there), and so does a second `CREATE` or
`CREATE OR ALTER` of the name (SQL71508). The idiom lives in a folder of deployment scripts, or in a project's pre- or
post-deployment script, which neither the build nor haruspex reads. A DACPAC holds only the final model, so nothing changes in
reading one.

**In the code:** `ScriptDefinitions`, `ScriptOutcome`, `DefinitionsInEffect`, `SchemaDeclarations.Settle`; observed in
`ExecObservationTests.Definitions.cs`, tested in `DefinitionsInEffectTests`.
