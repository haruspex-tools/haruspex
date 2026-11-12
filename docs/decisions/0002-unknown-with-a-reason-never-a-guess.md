# Never claim more than the engine guarantees

**Date:** 7 Nov 2026

**Decision.** Every claim haruspex makes must hold for every execution. A field reported non-null is never null, and a
value set holds every value the code can produce. What cannot be proved is *unknown*, with a reason from a closed list
(`IrUnknownReason`), a sentence saying why, and where in the source it arose.

**Context.** No static analysis of a real language that always terminates can be both sound (every claim true) and
complete (every true fact claimed): Rice's theorem rules it out. So a tool must choose which way to be wrong when it
cannot tell.

**Alternatives.**
- *Guess the likely type*, as a heuristic or a model trained on examples would. Most fields would come out right, and the
  wrong ones would be found by a user at runtime, in their code, with no sign of which they were.
- *Sound except for known holes* ("soundy", in Livshits et al.'s word), trusting the generated runtime check to catch a
  wrong claim. The check turns a wrong claim into a failed parse of every row that breaks it: an outage the user can only
  fix after it happens, with an override.
- *Fail the whole procedure* when any part is not understood. Sound, but one unmodelled column would hide every field that
  was proved.

**Reason.** The two errors don't cost the same. A claim that is too cautious costs a `.nullable()` the developer can see,
and override with a reason. A claim that is wrong costs a failure in production. A user forgives the first and not the
second. Keeping each unknown local (a field, a result, a procedure's results) keeps everything around it typed, and the
reason tells the user what would resolve it.

**See also:** [the guide, "The shape of the problem"](../internals/01-the-shape-of-the-problem.md);
[claim everything it does guarantee](0005-claim-everything-the-engine-guarantees.md).
