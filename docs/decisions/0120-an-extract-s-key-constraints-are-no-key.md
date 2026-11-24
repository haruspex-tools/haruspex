# An extract's key constraints are no key

**Date:** 24 Nov 2026

**Decision.** In a DACPAC extracted from a database, a primary key or unique constraint is no key: nothing is claimed of it,
so a query that fixes its columns is not claimed to return at most one row. The maintainer chose this rule, the strict one, on
24 Nov 2026: the package can't say whether the constraint's index is enabled. A unique index created apart from a constraint
is read as the package marks it: an extract marks one that is disabled (`IsDisabled`), so one not marked is enabled, and its
key holds. A package built from a project keeps every key.

A package is taken for an extract unless its `Origin.xml` says it was built: it names no server, and its product is DacFx's
build task, which `Microsoft.Build.Sql` and SSDT's legacy projects run. A package written by DacFx's API, as community SDKs
build one, keeps no key constraint either, since the API writes the same `Origin.xml` for an extract it loads and writes again;
nor does one without an `Origin.xml`.

The config file's `builds` lists packages, by file name, that the user declares built from a project. Their primary keys and
unique constraints are then claimed. That is the user's assumption, as a declared session is: haruspex can't check it, and the
guarantee rests on it. Its checks are read as before, by what the package says made it. The maintainer added the setting the
same day: a disabled key is rare, and a team whose packages a community SDK builds should be able to keep its key claims. Checks keep
their own rule, the other way round: only a package that names its server is an extract, whose checks are all read, since only
an extract marks a check the server doesn't trust.

**Context.** `ALTER INDEX … DISABLE` on the index of a nonclustered primary key or unique constraint leaves the constraint in
place and stops it being enforced, so the table can hold duplicates (observed: two rows with one key, inserted after the
disable). An extract of that database writes nothing on the constraint: the primary key's element says only that it is
nonclustered, and the unique constraint's has no properties, exactly as when the index is enabled. The same extract writes `IsDisabled` on a
unique index disabled on its own (observed, in `tests/dacpacs/versions/atelier-extract.dacpac`, which `build.sh extract` makes
after disabling all three). A build can't hold such a constraint: DacFx refuses `ALTER INDEX` on a constraint's index
(SQL71001), from a project and from its API alike, and SQL Server enforces the deployed constraint. Disabling the index leaves
the primary key's columns NOT NULL (observed: an insert of NULL is refused, 515), so a primary key still makes its columns
non-null.

Every extract observed names its server in `Origin.xml` and `Microsoft.SqlServer.Dac` as its product; a build by
`Microsoft.Build.Sql` names `Microsoft.Data.Tools.Schema.Tasks.Sql`, without a server. DacFx's API names
`Microsoft.SqlServer.Dac.Extensions`, without a server, both for a model built from scripts and for an extract loaded by the API
and written again, whose constraints still carry no mark (observed), so the two can't be told apart.

**Alternatives.**
- *Take an extract's key constraints as enforced, and say so as an assumption.* It keeps the claims most databases deserve,
  since a disabled key index is rare, but where it fails Zod refuses the real result at run time, and nothing in the package
  lets haruspex tell such a database from another.
- *Take a package that names no server for a build, as checks do, or take DacFx's API for a build.* An extract written again
  by the API, a package from an unknown tool, or one with its `Origin.xml` removed would then keep keys nothing vouches for.
  Taking it for an extract here costs only claims, though it costs a community SDK's packages their key claims.
- *Read the disabled state from the database.* That needs a connection, and haruspex reads only source.

**Reason.** A claim haruspex makes must hold for every execution. An extract can't tell an enforced key from one that isn't, so
claiming it would rest on a guess. A team that wants the claims builds its package from the project, whose keys hold.
