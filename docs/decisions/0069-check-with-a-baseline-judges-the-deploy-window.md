# `check` with a baseline judges the deploy window, beside the comparison with the output folder

**Date:** 18 Nov 2026

**Decision.** `haruspex check` takes a baseline: `--baseline <source>`, or `"baseline"` in the config file, the command line
winning. It is any source the `source` setting takes (a folder of `.sql` files, a `.sqlproj`, a `.dacpac`) or an IR file of
this version. It is never fetched: a step before the check puts it there. The check then asks whether the database
being checked fits within the baseline, as the application already running, built against it, meets it
([decision 0068](0068-a-baseline-fits-when-the-running-application-accepts-every-value.md)):

- **A change that application would fail on is an error** (HAR6026), at its procedure, and fails the check with exit code 1.
  **One it sets aside is a warning** (HAR6027), and fails it only under `"objects": "strict"`, where it is an error instead.
- **A function whose return shape changes is a warning** (HAR6028), when the module writes the functions: the application
  running keeps its own functions, but each call changes at its next build
  ([decision 0060](0060-a-call-returns-its-one-result-itself.md)). A result set's name that is given, changed or taken away
  ([decision 0070](0070-a-procedure-s-result-sets-are-named-all-or-none.md)) is such a change, and only that: the server
  sends sets by place, so the running application doesn't see it.
- **The comparison with the output folder is still made**, so the application's CI checks both that the committed module is
  current and that the change is safe to deploy. **`--fit-only` makes the fit alone**, without reading the output folder:
  a database's pipeline checking an application's committed contract asks for it, since the exact comparison with that
  contract would refuse every change the fit allows. Without a baseline it is a usage error (HAR6032, exit code 2).
- **A source baseline is read with the module's own config file and command line**, so the two documents are selected,
  overridden and named alike, and its warnings aren't said (they are about the old source). An IR baseline is read as it is,
  and one of another version is refused (HAR6030), as the output folder's is.
- **A query file is judged only where its text is known to be what the application running sends**: with an IR baseline, when
  the text hasn't changed. Otherwise it is a warning (HAR6031), not judged.
- **`--baseline` is for one module**, like a source and `--out`; with several, each config file names its own.

**Context.** The settled design (18 Nov 2026) asked for a baseline setting taking any source, with the command line winning,
haruspex never fetching, and `check` failing on what the running application's schemas would refuse and warning on what they
ignore. Richard added the database's side: the same check in the database's pipeline, against each application's committed IR,
refusing a deploy that would break an application already running.

**Alternatives.**
- *The baseline in place of the output folder.* One question per run, but a `"baseline"` in the config file would then stop the
  application's CI checking that the committed module is current, and V1b's `generate`, which makes types covering both, needs
  the comparison with what it wrote.
- *Make the fit alone when the baseline is the output folder's own IR*, with no option. In a database's pipeline the
  application's IR is usually copied or fetched somewhere else, so the paths wouldn't match, the exact comparison would run,
  and every additive change would fail; a link or another spelling of the path fails the same way. A behaviour that changes
  because two paths happen to match is also hard to explain. An option says what is asked.
- *Read git refs (`"baseline": "git:v1.4.0"`).* The tool would run git and build a project from another commit, which is more
  than it should own. The recipes in [the command line](../command-line.md#checking-against-a-baseline) do it in CI instead.
- *Infer the baseline with its own release's config file.* Not at hand for a DACPAC from a release pipeline, and a config file
  that changed (a new override) would show up as a change to the database.
- *Judge every query file as procedures are judged.* The application running sends its old text, which neither document
  describes against the new database once the file has changed; judging it would claim what wasn't shown.

**Reason.** An application and its database deploy separately, and the check is how a change is made safe for the window
between them: expand and contract, enforced mechanically, with each step said as the change.

**In the code:** `BaselineCheck` and `Check` in the CLI, `HaruspexConfig.Baseline`; [HAR6026](../diagnostics/HAR6026.md) to
[HAR6032](../diagnostics/HAR6032.md).
