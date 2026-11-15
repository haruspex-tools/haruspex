# A star is bound against the tables the project defines

**Date:** 15 Nov 2026

**Decision.** `SELECT *` and `alias.*` are expanded wherever a query stands, in every kind of module, against each table as the
project defines it: its columns in the order it declares them, without a `HIDDEN` column, and with a column set in place of its
sparse columns. A temporary table's star is its columns as the walk knows them at that statement. One rule for every module: a
procedure, a scalar function, an inline function, a multi-statement function, a view and a trigger alike. Where binding can't
list a star's columns (a table not in the project, a caller's temporary table, a graph table, a qualifier of two parts), the result
is unknown with that reason, as before.

**Context.** When SQL Server binds a star depends on the module. Observed on 15 Nov 2026 on SQL Server 2025 RTM-CU10
(`SelectStarObservationTests`, `ApplyViewObservationTests`), by creating a module over a table, then adding a column to it:
- **A procedure, a scalar function and a multi-statement function** bind it when they run: the new column is in what they return
  (a multi-statement function's `INSERT … SELECT *` then fails, 213), even with a plan cached before the change.
- **A view and an inline function** bind it when they are created, and keep that list until `sp_refreshsqlmodule` (or
  `sp_refreshview`) binds it again.
- **A schema-bound module** can't have a star at all (1054), not even inside `EXISTS`; `COUNT(*)` is no star and is allowed.
  So a module protected from its tables changing never has a star, and a module with one is never protected. A natively
  compiled procedure must be schema-bound, and refuses a star the same way, in `EXISTS` too (observed by hand, since it needs
  a memory-optimised filegroup). Any other procedure can't be schema-bound (10796), so its star is always bound when it runs.

So for a view or an inline function, what it returns depends on whether it was refreshed after its tables last changed, which
the project doesn't say. What deployments do, observed the same day:
- **SqlPackage 170.2.70**, publishing a package whose table gained a column, ends its script with `sp_refreshsqlmodule` for the
  view, the inline function and the procedure that read it, and after the publish each returns the new column.
- Its publish rebuilds a table into the declared order when a column is added between two others; with
  `/p:IgnoreColumnOrder=True` it adds the column at the end instead, so `column_id` order then differs from the declaration.
- A deployment of hand-written scripts does whatever its scripts do.

**Alternatives.**
- *Expand the star of a procedure and of the functions bound when they run, and leave a view's and an inline function's
  unknown.* Sound under any
  deployment, but it throws away most views over a star, which are common, and the refresh that makes them current is what the
  usual tool does unasked.
- *Unknown wherever a table could have changed.* Every table could have, so it would never expand a star; nothing would be
  gained over the unknown this replaces.
- *Read what the module was bound to.* A project holds no record of it, and a DACPAC holds the module's text, not its bound
  columns.
- *Refuse positional uses of a star* (a set operation, an `INSERT` without a column list, a column list naming a star's
  columns), which a deployment that appends columns would misalign. A rowset and a `FOR JSON` object are keyed by name, so their
  shape doesn't depend on the order; the positional uses are where the order matters, and they are rarer. Refusing them would
  be sound under that option, but would leave unknown what every deployment in the declared order gets right.

**Reason.** The project describes the database after deployment, and haruspex describes what each module returns against that
database: so a star stands for each table as the project defines it, in every module, as it already did for views. For a
procedure and the functions that bind at run time this is the engine's own rule; for a view and an inline function it is the
assumption that the deployment refreshes them, which SqlPackage does; and the declared order is assumed to be the deployed one,
which its default publish keeps. Schema binding doesn't enter the rule: no module that has a star can be schema-bound, so a
star gone stale is a question only for a view or an inline function that isn't. A deployment that leaves a module bound to an
older table, or that adds columns out of their declared order, is not described, and the reference says so.

**See also:** the inference reference's "SELECT *" section; [Binding](../internals/03-binding.md), "SELECT *"; the
`haberdashery` fixture.
