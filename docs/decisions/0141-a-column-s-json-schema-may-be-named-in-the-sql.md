# A column's JSON Schema may be named in the SQL

**Date:** 27 Nov 2026

**Decision.** The extended property `haruspex.jsonSchema` on a table's column names the JSON Schema of the JSON it holds, as a
path relative to the config file's folder with an optional JSON Pointer after `#`, the same reference `columns` takes in the
config file. It declares exactly what that `columns` entry would
([decision 0093](0093-a-column-s-json-is-part-of-its-type-completed-by-the-config.md)), and is recorded the same way: a
`columns` entry in the IR with the hash of every file read, and a `column-json` claim on each object that reads it. The IR doesn't
say where a declaration was written, so it needs no new version. It is read from scripts and from a package by the reader of
[decision 0128](0128-a-column-s-description-reaches-the-fields-that-are-that-column.md). Where the config file and the SQL both
declare a column, they must name the same file and pointer; otherwise the run stops (HAR4114) and neither is taken.

What the server takes it on, and what haruspex does with each (observed on SQL Server 2025, and in a package built with
Microsoft.Build.Sql 2.3.0):

| Where | Taken by the server | haruspex |
|---|---|---|
| A table's column | yes | declares it (HAR4113 where `columns` would refuse it: computed, not text) |
| A view's column, a table function's column | yes | HAR4113: its column holds what it reads, so declare the table column |
| A procedure's or function's parameter | yes (with `@`; without it, 15135) | HAR4116, a warning: it declares nothing |
| A numbered procedure's parameter (`get_order;2`) | no (15135; a build refuses it, SQL71501) | HAR4115: the source doesn't define it |
| A scalar function's result | no level exists | |

A package names a table's, a view's and a table function's column alike (`[SqlColumn].[shop].[f].[col].[…]`), and a procedure's
and a function's parameter alike, so the reader looks the owner up among what the package defines. A package may set a property
on what another package of the same database defines, so an owner the package doesn't define is settled once every package is
read; one nothing defines is then found missing and said (HAR4115 here, HAR1024 for a description), never passed over. Every
other host a build writes (a table type's column, a function, a type, an index, a schema) is read with the levels a script's call
would give, so that a folder and its package say the same.

**Context.** `columns` keeps a column's schema in one application's `haruspex.json`. A team whose database is shared, or shipped as
a DACPAC, wants the schema with the column, where every application that reads it will find it. The design anticipated this
("the declaration could also live in the database as an extended property") and said the config would win where both exist.

**Alternatives.**
- *The config file wins where both declare a column*, as the design first said. A silent choice keeps one declaration and ignores
  the other, which may be the one the writers follow; a disagreement is a mistake the team should see.
- *Compare the two by their translated types*, so that two files of the same text agree. The IR records the file, and `check`
  follows it; two files that agree today may not tomorrow, and nothing would say which one a later change should go to.
- *A configurable property name*, as `descriptions` is. A description's name is set by other tools, which differ; this property is
  haruspex's own, and its prefix keeps it from meaning anything else.
- *Read it on parameters too.* A parameter's JSON comes from each caller, and declaring it needs the inference to start a
  parameter's value from a schema, the IR to record a declaration that isn't a column's, and the emitters to type an input by it.
  That is a change of its own, with an IR version; until then a parameter's annotation is a warning, not a silent nothing.
  Provisional, for the maintainer's word: the coordinator agreed this narrowing to a table's column on 27 Nov 2026.
- *Fetch a schema named by URL.* A build would then depend on someone else's server, and the schema could change under the same
  name; it is refused (HAR3112), as every remote schema is.
- *A comment in the script* (`-- haruspex:`). A package keeps no comments outside a module's text, so it couldn't carry it; it may
  come later for what extended properties can't reach.

**Reason.** The annotation is the same claim as a `columns` entry, made in another place. Reading it into the same declaration
keeps every rule about declared JSON in one place, and refusing a disagreement keeps the claim one the team made once.

**What it costs.** A team that annotates a column and also declares it in the config file must keep the two the same. An
annotation on a parameter is read and then set aside. Generating needs the schema files at the paths the database names, relative
to each application's config file.

**In the code:** `ColumnAnnotations`, `ColumnSchema` and `ColumnDeclarationCheck.Problem` in the config layer;
`PackageProperties` in the package reader; `SqlProject.Infer`; `ColumnAnnotationTests`, `PackagePropertiesTests` and
`ColumnAnnotationObservationTests`; the fixture `parcels`, whose `route` is named in its table's script.
