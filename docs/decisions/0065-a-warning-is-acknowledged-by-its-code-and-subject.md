# A warning is acknowledged by its code and subject, and a missing `master.dacpac` stays a warning

**Date:** 17 Nov 2026

**Decision.** The config file's `acknowledge` lists warnings a team has read and lives with, each by its code and its
subject together, with why: `{ "code": "HAR1008", "subject": "StampBuildInfo", "why": "…" }`. An entry without a subject or
a reason is refused. An acknowledged warning is counted in the summary rather than said, listed with `--verbose`, held apart in
`--format json` (`acknowledged`) and written to SARIF as a result with an external suppression. Every other warning of the same
code is still said. Only warnings about the source that name a subject can be acknowledged: a referenced DACPAC or project not
found (HAR1003, HAR1007, by its file name), a build target that isn't read (HAR1008, by its name) and a write to an object the
project doesn't define (HAR2101, by the object's name, its parts joined by dots without brackets). Subjects are compared
ignoring case, as SQL Server compares names, so one table is one subject however a procedure spells it. An entry for any other code is an error (HAR6024), and an entry that matches
no warning in a run is a warning (HAR6025), except in a run whose `--include` narrows the selection.

HAR1003 for a missing `master.dacpac` stays a warning, acknowledgeable like any other.

**Context.** A consumer's trial of the npm preview found warnings it could do nothing about said on every run: HAR1008 for a
custom MSBuild target that adds no scripts, and HAR1003 for the `master.dacpac` an SSDT project refers to, which only a Visual
Studio install has. A warning repeated on every run, with no way to say it has been read, teaches people to skip warnings, and
the next one that matters is skipped with them. Each diagnostic carries a subject for this (`Diagnostic.Subject`), set by the
layer that reports it, so an entry matches a warning exactly, not by its message's words, which may be reworded.

**Alternatives.**
- *Acknowledge by code alone*, as a compiler's suppression of a warning number does. One `HAR1008` would hide every target not read, including the
  one added next month that does add scripts. A subject keeps the acknowledgement to what was read.
- *Match the subject against the message's text.* No code to give each diagnostic a subject, but a message reworded, or a
  subject that is a substring of another's, would make an entry match nothing or the wrong warning.
- *Acknowledge any warning.* A warning about the config file (an override the inference already proves, a declaration nothing
  uses) is fixed in the same file the entry would be written in; an acknowledgement would only hide it.
- *Say nothing of an entry that matches nothing.* An entry left behind after its cause is gone would quietly hide the next
  warning of that subject.
- *Make HAR1003 for `master.dacpac` a note.* What the package gives is the columns of the system's views (`sys`,
  `INFORMATION_SCHEMA`), by name and untyped. Without it, a procedure that reads one gets an unknown that says "there is no
  table sys.objects in the project", which doesn't name the missing package; the warning is the one place that says why. It is
  also actionable: a folder holding a `master.dacpac`, such as the `Microsoft.SqlServer.Dacpacs.Master` package's, named in
  `references`, gives it. Once the unknown itself names the missing package (a change to inference's messages), the warning
  could become a note; until then, a team whose procedures read no system view acknowledges it.

**Reason.** A team should be able to say "read, and fine" once, in the file it reviews, with why, and see the rest. Doing so by
code and subject keeps the acknowledgement as narrow as what was read, and saying an entry that has gone stale keeps the file
honest.

**In the code:** `Acknowledgements` and `AcknowledgedWarning` in `src/Haruspex.Cli`, `Acknowledgement` and its reading in
`ConfigReader` in `src/Haruspex.Config`, `Diagnostic.Subject` and `DiagnosticCode.At(at, message, subject)` in
`src/Haruspex.Diagnostics`.
