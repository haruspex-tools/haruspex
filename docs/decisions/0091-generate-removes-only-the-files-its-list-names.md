# `generate` removes only the files its list names

**Date:** 21 Nov 2026

**Decision.** `generate` keeps a list of the files it wrote, `haruspex.files.json`, in the output folder beside them: the IR and
each emitted file, by its path relative to the folder with `/` between names, sorted, so that the list is the same on every
platform. After the module is written, it removes each file the last run's list names that this run didn't write, then writes
the list anew. The list is the only thing it trusts for removal: it never looks for files to remove in the folder.

- **Nothing is removed** when the run fails before writing, when there is no list (the first run, or the first after
  upgrading), or when the list can't be read ([HAR6039](../diagnostics/HAR6039.md)), which is written anew all the same.
- **Never outside the folder.** An entry that is absolute, has a `..`, has a character no name `generate` writes has (ASCII
  letters, digits, `.`, `-` and `_`, names joined by `/`), or is reached through a
  symbolic link or junction at any step, whether it points out of the folder or not, is never removed
  ([HAR6040](../diagnostics/HAR6040.md)). An entry that is a folder, or is gone, is passed over without a word.
- **Never a folder.** Removing a file never removes the folder it was in, even when that leaves it empty: `generate` writes no
  folder but the output folder, so any other is someone else's.
- **A file edited by hand is still removed** when the list names it: the outputs are generated, and each says so (a comment at
  the top of the TypeScript, the JSON Schema's `description`, the list's `about`).
- **Case.** An entry this run writes again is not removed, however its case differs, where the folder's file system ignores
  case: a module renamed `Admin` to `admin` keeps the file this run wrote. Where the file system tells case apart, the old file
  is a file apart, and is removed. Holding entries to ASCII is what makes this exact: such a file system folds other letters its
  own way (Windows' and macOS's take the Kelvin sign for `K`), so an entry with one could name a file this run wrote under a
  name that compares as another.
- **A run narrowed by `--include` removes nothing**, and carries what it didn't write onto the new list. It is a one-off look at
  part of the module; removing whole modules because of it would be a surprise, and the next full run removes what is gone.
- **A file that can't be removed** stays on the new list, so the next run tries again ([HAR6041](../diagnostics/HAR6041.md)).
- **The list is written last**, after the removals, so a run stopped before then leaves the last run's list, which still names
  every file this one would have removed.
- **`check` compares the list** as it compares the other files: a missing list is nothing to compare with
  ([HAR6011](../diagnostics/HAR6011.md)), and a file the list names that `generate` no longer writes is an error at that file
  ([HAR6042](../diagnostics/HAR6042.md)).
- **A run with a baseline** that leaves a procedure out of the module (HAR6033) still writes the module, and so removes what it
  no longer writes, a module the baseline emptied included.
- The "Wrote" line doesn't name the list, which is bookkeeping; a run that removes files says which.

**Context.** A module taken out of `split`
([decision 0074](0074-a-config-may-split-its-output-into-modules.md)), or emptied by a baseline, and a file of an emitter taken
off `emit`, stayed in the output folder after `generate` stopped writing them. They no longer matched the IR; the run failed, so
CI caught it, but the files stayed until someone removed them by hand.

**Alternatives.**
- *Remove whatever in the folder matches the names haruspex writes* (`*.zod.ts`, `*.jsonschema.json`). The folder may hold the
  application's own files, and a name is no proof of who wrote a file: a glob would one day remove a file haruspex never wrote.
- *Record the modules in the IR.* The IR is a claim about the database, and a module is a way of writing it out
  (decision 0074); the list of files records what was written, which is what removing needs, for emitters too.
- *Leave removing to the user*, as before. Every reviewer of a removed module's diff would have to know which files went with
  it.
- *Remove the folder a removed file leaves empty.* `generate` never writes into a folder below the output folder, so the only
  way an entry is in one is an edit; removing someone else's folder because a file in it went is a surprise for no gain.
- *Refuse only a link that resolves outside the folder.* Resolving a path is itself the risk: one that points inside today may
  point out tomorrow, and `generate` never writes a link, so any link on the way means the entry is not its own.

**Reason.** A tool may remove only what it can prove it made. The list is that proof, written by the run that made the files,
committed with them, and checked by `check` as they are; the folder's contents prove nothing. Every rule beyond that errs toward
leaving a file, since a stale file is reported by `check` and a removed one is lost.

**In the code:** `OutputList`, `StaleFiles`, `Generate.Tidied`, and `Check.NoLongerWritten`.
