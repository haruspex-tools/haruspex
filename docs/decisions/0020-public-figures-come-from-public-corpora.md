# Public figures come only from public corpora, written by a test

**Date:** 11 Nov 2026

**Decision.** Every coverage figure in haruspex's public text is measured on Microsoft's sample databases (AdventureWorks
and WideWorldImporters), vendored in the repository at a pinned commit, and written to
[the corpus figures](../corpus-figures.md) by a test. Any other page that states a figure copies it from there, and a test
checks the copy. Figures measured on private code never appear in public.

**Context.** A figure such as "typed 32 of 38 fields" is the kind of claim the style guide asks for in place of "types
almost everything", and it is only as good as the reader's ability to check it. A figure from a private corpus can't be
checked by anyone outside, and says something about code nobody else can see. A figure written by hand goes stale the
first time inference changes, and nobody notices, because nothing fails.

**Alternatives.**
- *Figures from private corpora, described but not shown.* They may be more representative of real use, but no reader
  can reproduce them, and they leak facts about someone else's code.
- *A figure written by hand, refreshed now and then.* It drifts between refreshes, which breaks the documentation's own
  rule of no claim stronger than the evidence.
- *Downloading the corpora when the test runs*, or a git submodule. A test that needs the network fails offline and can't
  pin what it measured; a submodule can't take one folder of a repository of about 500 MB. Vendoring the subset, with a
  manifest of hashes and a script that refreshes it, keeps the input fixed and an update readable as a diff.
- *Other public corpora.* There are others, of many sizes and styles. These two are Microsoft's own samples, the ones a
  SQL Server developer is most likely to know, under the MIT licence, and they come in the two forms haruspex has to read:
  a database project (WideWorldImporters) and one install script (AdventureWorks).

**Reason.** A figure that a test writes can't drift from the code, and one measured on public code can be reproduced by
anyone with a checkout. When inference changes, the test fails until the page is rewritten, so the change to the figures
shows in the same diff as the change to the code.

**Amended** (15 Nov 2026). The rule is public, pinned and written by a test, not Microsoft's samples only. A public figure
comes from any public corpus, vendored or fetched, at a pinned commit, written by a test. Microsoft's two samples stay
vendored, because the guide's worked examples and the project's README rest on them and must run offline on every build.
Every other corpus is fetched: a manifest in the repository names its repository, commit, licence, the paths taken and each
file's SHA-256, and a script fetches it into a folder outside the checkout, checking every hash. Only counts are committed
from a fetched corpus ([the harvest](../corpus-harvest.md)), never its code, names, IR or generated code, so haruspex
redistributes nobody's work, and a corpus under a copyleft licence can be measured without any of it entering the
repository. A corpus's licence or terms must allow downloading it for analysis; a repository with no licence is not used
until that is settled, since publishing counts from it is the project's act. A test over a fetched corpus is skipped,
saying how to fetch it, where it hasn't been fetched; CI fetches first and fails rather than skips.

This narrows the alternative rejected above, downloading the corpora when the test runs. What the rejection feared holds
no longer: the manifest pins every byte measured, so a fetch can't measure anything but the commit it names, and offline a
test is skipped rather than failing. Fetching is also what lets the corpora grow past what may be vendored: a repository of
hundreds of megabytes costs a sparse, shallow fetch of the paths it uses, and a licence that would forbid vendoring forbids
nothing here.

**In the code:** `CorpusFiguresTests`, `CorpusHarvestTests`, `tests/corpus/fetched.json` and `tests/corpus/fetch.mjs`, and
[`tests/corpus/README.md`](../../tests/corpus/README.md).
