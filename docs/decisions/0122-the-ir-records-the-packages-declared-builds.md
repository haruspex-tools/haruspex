# The IR records the packages declared builds

**Date:** 25 Nov 2026

**Decision.** From IR 0.31, a source records in `source.builds` the packages the config file's `builds` declared builds that it
read, by the names its `package` and `references` give them, sorted. A package `builds` names that the source didn't read is
left out, since nothing was assumed of it. A package that is a build by its origin is recorded too when `builds` names it:
the record says what the user declared, not whether the declaration was needed, so removing such a name fails `check` though
no claim changes, which errs towards a check that fails rather than one that passes. A document that records one is labelled 0.31; one that records none is still 0.30, as
every document that says less keeps the oldest label that says all it says
([decision 0072](0072-a-document-written-with-a-baseline-is-the-combined-one-and-says-so.md)). The reader refuses `builds` in a
document labelled earlier, a document labelled 0.31 that records none, and a name that is neither the source's package nor a
reference found. `check` compares the declaration as a claim: a package declared a build, or no longer, is a change, reported
as `source.builds["sales.dacpac"]: no longer declared a build`. The maintainer asked for this on 25 Nov 2026 ("Moving the IR
version is fine, especially as there are no users").

**Context.** A declared build is the user's assumption ([decision 0120](0120-an-extract-s-key-constraints-are-no-key.md)): its
primary keys and unique constraints are claimed though nothing in the package says what made it. Until 0.31 the assumption was
kept in the config file alone, so a generated file didn't say it rested on one, and a change to `builds` showed in `check` only
through the claims it changed. Where those were none (a key no selected procedure fixes), nothing showed.

The callers' session is recorded already, in `source.session`, and `"unmask": true` joined it without moving the version
([decision 0118](0118-a-masked-column-claims-only-its-type-nullability-and-greatest-length.md)). `builds` doesn't fit there:
`session` maps the name of a session option to on or off, by the dialect's name for each, and every reader of 0.30 reads it so.
A package's file name is no option of the session, and a list of them is no map to booleans. Written as `"sales.dacpac": true`
it would read as an option a 0.30 reader has never met, with no way to tell the two apart.

**Alternatives.**
- *Record it in `source.session`.* No version move, but the map would hold two kinds of key, and a package named as a session
  option is wrong to every program that reads the map as its schema describes it.
- *Mark each package where the source names it* (`"build": true` on a reference). The source's own package is two strings,
  `package` and `sha256`, with no object to mark, so it would need a third property beside them; a list in one place is simpler
  to read and to compare.
- *Record `builds` as the config file writes it*, every name, read or not. A name that matches no package would then change the
  document and fail `check` while changing no claim, and the names would be the config file's spelling, which ignores case,
  rather than the packages'.

**Reason.** A document should say what its claims rest on, and the reader holds every document to what its label says. A
property of its own says exactly which packages the claims trust, and only those, under a label older readers refuse rather
than misread.
