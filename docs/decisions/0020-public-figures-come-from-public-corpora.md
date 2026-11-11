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

**In the code:** `CorpusFiguresTests`, and [`tests/corpus/README.md`](../../tests/corpus/README.md).
