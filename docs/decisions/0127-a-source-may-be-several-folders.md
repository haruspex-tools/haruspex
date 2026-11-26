# A source may be several folders

**Date:** 26 Nov 2026

**Decision.** The config file's `source`, and the command line's source argument, may name several folders of scripts, read as
one database. Every entry must be a folder: a `.sqlproj` or a `.dacpac` in a list is an error naming it. A list of one is
that folder. The folders are read together, and no script may be read twice: two entries naming one folder (in any spelling
the file system takes for the same, its case included where it ignores case), or one entry inside another, is an error naming
both. A folder that two entries reach through a link is one too.

From IR 0.32, a source read from several folders records them in `source.paths`, each as `path` writes one (relative to the
config file's folder, with `/`), sorted, and leaves `path` out. A source read from one folder still records `path` alone, so a
document of one folder is labelled as before: 0.32 only when a document records `paths`
([decision 0072](0072-a-document-written-with-a-baseline-is-the-combined-one-and-says-so.md)'s rule, as
[decision 0122](0122-the-ir-records-the-packages-declared-builds.md) applied it). The reader refuses `paths` before 0.32, a
0.32 document that records none, a list of fewer than two, an entry twice, and `path` beside `paths`.

A location in a document read from several folders is relative to the config file's folder, as a query file's already is, so
that it says which folder its script is in (`tables/orders.sql:3:5`). A document of one folder keeps its locations relative
to that folder.

`check` compares the folders read as a claim, wherever either document records `paths`: a folder read in one and not the
other is a change, reported as `source.paths["db/tables"]: no longer read`. A document of one folder against another of one
folder compares no paths, as before: renaming the database's only folder changes no claim.

The maintainer asked for a list on 26 Nov 2026, for a database kept as folders side by side (`tables/`, `procedures/`, beside the
application's `queries/`), which until then had to be moved into one folder to be read.

**Context.** A database's scripts are often kept as folders beside each other and beside the application's own query files.
Pointing `source` at their common parent reads the query files as the database's; moving the folders changes the user's
repository to suit the tool. `init` advised the move.

What the claims rest on is that every script of the database was read, and only once. One folder walked once guarantees the
second already (each folder entered once by where it really is). Several folders don't: one inside another, or one listed in
two spellings, reads its scripts twice, and a script read twice defines its objects twice (HAR1002), so the read stops rather
than guesses. Saying so up front, naming both entries, is clearer than a duplicate definition per object.

Which folders were read is part of what a document describes, as which packages are declared builds is: a folder added may
hold a trigger or a constraint that changes no field a module shows but changes what the database does. Reporting it errs
towards a check that fails rather than one that passes.

**Alternatives.**
- *`path` as a string or an array.* One property, but a 0.31 reader would meet an array where its schema says a string, and
  every program that reads `path` would need to tell the two apart. A property of its own is refused by older readers rather
  than misread.
- *Record the list in every document, one folder included.* Simpler to read, but every document read from a folder would
  change, and be labelled 0.32, for nothing it says.
- *Locations relative to each folder, as before.* Two folders may each hold `procedures.sql`; a location would no longer say which
  file it means.
- *Compare `path` in every document.* Renaming the one folder of a project, or moving its `.sqlproj`, would fail `check`
  where no claim and no script changed.
- *Allow a list for `baseline` too.* Nothing asks for it yet; a baseline of several folders can be added the same way.

**Reason.** The list lets a repository stay as it is, and the IR says which folders its claims rest on in a way that leaves
every document of one source as it was.
