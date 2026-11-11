# Claim everything the engine guarantees

**Date:** 9 Nov 2026

**Decision.** Each emitter produces the most accurate schema its target can express. A fact the IR carries (a length, a
range, a decimal's step, a value set, whether a key may be absent) becomes a check in the generated code wherever the target
can hold it exactly, and a comment saying why where it can't.

**Context.** [Soundness](0002-unknown-with-a-reason-never-a-guess.md) says what must not be claimed. It says nothing
against throwing a proved fact away, and a generator that types every string as `string` is sound.

**Alternatives.**
- *Types only* (`string`, `number`, nullable or not). Simpler output, but a value that breaks a `CHECK` or a length would
  pass, and the reader would have to look up the schema to know the limits.

**Reason.** Discarding something known is a defect, as claiming something unknown is: the application loses a check the
database already proved, and the generated types say less than the SQL does. The one exception is deliberate and recorded:
[objects strip unknown keys by default](0008-objects-strip-unknown-keys-by-default.md).

**In the code:** [`src/Haruspex.Emit.Zod/README.md`](../../src/Haruspex.Emit.Zod/README.md).
