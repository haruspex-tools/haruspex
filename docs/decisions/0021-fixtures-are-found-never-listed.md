# Fixtures are found by listing their folder, never named in a list

**Date:** 11 Nov 2026

**Decision.** Each fixture is one folder under `tests/fixtures`, holding its schema, its expected outputs, its seed and its
runs. Every test that needs "every fixture" finds them by listing that folder (`Fixture.All` in `tests/Shared`); no test
keeps a list of fixture names, procedures or runs of its own. A folder that lacks a part its kind needs fails discovery.

**Context.** The snapshot tests, the emit check, the differential tests' deployment, seeding and coverage, and the Zod
acceptance check each need the set of fixtures. Each kept its own list: a map of every procedure to its fixture, a list of
the fixtures to deploy, a list to compile, a folder of seeds and a file of runs. The lists drifted: the emit check left
out one fixture, so its generated Zod was never compiled, and nothing noticed.

**Alternatives.**
- *Keep the lists, and add a test that they agree.* It catches drift after the fact, and every new fixture still means
  editing several files in step.
- *One list in shared code that every test reads.* Better, but still a second copy of what the folder already says, and a
  fixture's parts stay in several places.

**Reason.** The file system already holds the fact; a list is a second copy, and two copies disagree sooner or later. With
discovery a new folder is snapshotted, compiled, deployed, run and measured for coverage by every test at once, and a
fixture's folder describes it whole. A fixture of a different kind says so in its own files (a project for the CLI has a
`haruspex.json` naming its `source`, and no seed).

**See also:** [the guide, "Observing the engine"](../internals/09-observing-the-engine.md);
[`tests/fixtures/README.md`](../../tests/fixtures/README.md).
