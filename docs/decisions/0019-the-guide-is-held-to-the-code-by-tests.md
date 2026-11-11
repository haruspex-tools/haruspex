# The guide is held to the code by tests

**Date:** 11 Nov 2026

**Decision.** The guide (`docs/internals/`), this log and the READMEs are written to be learnt from, and two tests keep them
true: every code reference in them (a path, a C# file, a type, a member, a test) must resolve, and every worked example (a
procedure and what haruspex produces for it) is run, and its output compared with the one shown.

**Context.** The project is meant to be read: by developers deciding whether to trust it, and by people learning how a static
analyser is built. So its documentation explains ideas in depth and points into the code.

**Alternatives.**
- *Documentation checked by review alone.* A rename or a changed rule leaves a sentence behind that no one rereads, and the
  documentation that explains most is the first to drift.
- *Generated documentation* from the code. It can say what the code is, not why it is so, or what was rejected.

**Reason.** Literate documentation that drifts is worse than none, since it teaches the reader something false with
authority. A test that fails on drift makes keeping the text true part of every change, like keeping the tests passing.

**See also:** [the guide's index, "How the guide is kept true"](../internals/README.md#how-the-guide-is-kept-true).
