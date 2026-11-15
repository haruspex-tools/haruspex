# Agent conventions

Instructions for coding agents working in this repository. Read [CONTRIBUTING.md](CONTRIBUTING.md)
first: its rules (the soundness principle, test first, decision context in code, how to build and test)
apply to agents exactly as to people, and are not repeated here.

In addition:

- **Never guess a type.** If a construct is not modelled, produce an IR unknown with a reason. Do not
  widen inference to make a test pass.
- **Snapshots are claims.** Never accept a `*.received.*` file without checking each change against what
  SQL Server actually does. Say in your report which snapshots changed and why.
- **Invented fixtures only.** Do not copy SQL, names or data from any real organisation's systems into
  this repository, including from other repositories on the same machine.
- **No new dependencies without asking.** In particular, never add DacFx (see CONTRIBUTING.md).
- **Never merge into `main` and never push.** The maintainer does both. Commit on your own `feature/` branch,
  each commit building and passing alone, with a truthful message. On `main`, do not commit: leave changes in
  the working tree and list the paths you changed.
- **Changes to `.claude/`, `.github/`, `AGENTS.md` and `CLAUDE.md` need the maintainer's review**
  (CODEOWNERS). Point them out explicitly.
- **Write to the house style.** Every comment, document and message follows [docs/style.md](docs/style.md).
  Run `tests/prose/check.sh` before handing over a change to prose, and fix the text rather than adding to the
  dictionary or the exceptions; add a word only when it is a real term.
- Keep the build free of warnings; they are errors here.
