# The command line's output is designed for a command run many times a day

**Date:** 17 Nov 2026

**Decision.** What `generate` and `check` print is designed, not left as a stream of every diagnostic.

- **A short default.** A run that succeeds prints its summary first, on standard output: what it typed and wrote, then what
  changed since the last run. Errors and warnings follow in full on standard error, as compilers write them. Notes, one for
  each unknown, are counted by reason, each reason's code linking its page; `--verbose` lists them, grouped by the object
  they are in, and `--quiet` prints errors only. A run that fails gives its errors, then one closing line, as a compiler
  does. Paths below the working directory are shown from it.
- **What changed since the last run.** `generate` reads the IR it is about to write over and compares it with the one it
  writes, claim by claim, as `check` does (decision 0050), and unknowns by their object, path and reason. A run that changes
  nothing is one line. A first run, or an earlier IR of another version or that doesn't read, says why it can't compare and
  counts the unknowns by reason instead.
- **Terminal only: colour, links and progress.** Colour follows `--color`, then `FORCE_COLOR`, `NO_COLOR` and `TERM=dumb`,
  then whether the stream is a terminal. A diagnostic code is an OSC 8 link to its page only with colour, never in CI, and
  only in a terminal that names itself as one that shows links (or with `FORCE_HYPERLINK`); otherwise one line under a list
  of codes points to the index of the pages. A progress line is drawn on standard error after half a second, only on a terminal and
  never in CI, and cleared before the report.
- **Link target.** A code's page is linked where it is on GitHub, on the repository's default branch: for HAR2013,
  [`docs/diagnostics/HAR2013.md`](../diagnostics/HAR2013.md) on `main`. The address is in one constant (`DiagnosticPages`).
- **Machine formats.** `--format json` writes one document for the run, specified by `schemas/haruspex.output.schema.json`
  and versioned by a `version` field. `--format sarif` writes a SARIF 2.1.0 log. `--format github` writes the text, then
  each error and warning as a GitHub Actions annotation (each note too with `--verbose`), and is the default when
  `GITHUB_ACTIONS` is `true`. The machine formats hold every diagnostic, whatever the text's detail.
- **No dependency.** The escape sequences, the JSON and the SARIF are written by the command itself.

**Context.** The command is run many times a day, by a developer after each change to the database and by CI on each push.
It used to print every diagnostic, one note for each unknown, then one line. A database with many unknowns printed them all
on every run, the same each time, so a new warning or a field that became nullable was hard to see among them. Richard asked,
on 16 Nov 2026, for a command line designed for that use: quiet when nothing changed, and saying what did. The quality bar
asks for GitHub annotations and SARIF for CI. The repository is private now and will be public.

**Alternatives.**
- *Every note by default, as compilers do.* tsc and the C# compiler print every diagnostic, but their warnings ask to be
  fixed. An unknown is haruspex's honest answer, not a mistake, and most stay for months: printed each run, they bury what is
  new. Counting them by reason keeps them visible without repeating them.
- *A summary last, as compilers do.* The last line is where the eye lands after scrolling, but a short default doesn't scroll,
  and the first line is what a developer glancing at a terminal or a CI log reads. A failure keeps the compiler's order,
  errors then the count, since the errors are what to read.
- *A diff of the files written, as `git diff` would show.* It would say that text changed, not what a reader of the module
  can rely on. The structural comparison `check` uses says "became nullable"; reusing it means one definition of a change.
- *The change report only on request (`--changes`).* What changed is the most useful thing a repeated run can say, so it is
  the default; the summary it replaces is still given when there is nothing to compare with.
- *A console library*, such as Spectre.Console. It is a large dependency for a few escape sequences, and one more to check
  for trimming and Native AOT. Writing the sequences is about forty lines, each tested.
- *Links wherever there is colour.* A terminal that doesn't support OSC 8 shows `]8;;https://…` as text in the middle of a
  diagnostic, which is worse than no link. No terminal says whether it supports them, so the list of those that do (the one
  the supports-hyperlinks package keeps for Node) is the evidence, and `FORCE_HYPERLINK` covers the rest.
- *Link targets.* A page pinned to the version's commit would match the binary exactly, but needs the commit at run time and
  gives a different URL to each version. A site of the project's own (`haruspex.tools`) doesn't exist yet. A path into the
  installed package works only where docs are shipped, and a binary has none. A code's meaning never changes (decision 0036),
  so the page on `main` holds for every version, and it is the address the documentation's own links already use. While the
  repository is private the links open for its collaborators only; when it is public they open for everyone, with nothing to
  change. A move to a site of its own is one constant.
- *A URL on each line of the reasons' table.* Long lines, each the same but for the code; one line pointing to the index,
  where each code links its page, says the same.
- *GitHub annotations only on request.* Then most workflows would never see them. As the default where `GITHUB_ACTIONS` is
  `true`, the text is still in the log and the annotations come with nothing to configure; `--format text` turns them off.
- *Annotations instead of the text.* GitHub shows a workflow command in the log as its message alone, without the file and
  line, so the log would lose them; and a step shows only the first few annotations of each kind. The text stays, and the
  annotations follow it.
- *A notice for every note in GitHub.* A hundred notices would hide the one error among them, and GitHub would drop most.
- *SARIF with one run for each module.* GitHub's code scanning wants one run for each tool and category in a file; the
  module is a property of each result instead.
- *JSON for each module apart (JSON Lines).* A tool parses one document with any JSON reader, and the document can say why no module ran.

**Reason.** A developer running the command for the tenth time today needs to see what is new, and what is wrong, at once.
Everything else stays one flag away, and nothing is lost: `--verbose` and the machine formats still say all of it. Colour,
links and progress help at a terminal and only get in the way anywhere else, so they are only at a terminal, by the
conventions other tools follow, and plain output is the same bytes on every machine.

**In the code:** `TextReport`, `JsonReport`, `SarifReport`, `GitHubAnnotations`, `OutputSettings`, `Progress` and
`DiagnosticPages` in `src/Haruspex.Cli/Output`; `SinceLastRun` and `EarlierOutput` in `src/Haruspex.Cli`.
[The command line](../command-line.md) says what each mode prints.
