# Every rule about the engine is observed on a real server

**Date:** 9 Nov 2026

**Decision.** Each rule haruspex relies on about SQL Server is observed in a differential test against a real SQL Server,
and the code that relies on it names that test. Every fixture is deployed, seeded and run, and every result is checked
against the IR that haruspex inferred for it and against the Zod generated from that IR.

**Context.** The claims haruspex makes are only as good as its model of the engine. SQL Server's documentation leaves much unsaid
(what `FOR JSON` writes for a query with no rows, which result set arrives where after a branch not taken), and what a rule
is believed to be is not evidence that it holds.

**Alternatives.**
- *Rules from the documentation*, checked by snapshot tests alone. A snapshot records what haruspex says, not whether SQL
  Server agrees: a wrong rule passes for ever.
- *Rules observed once by hand.* The observation is lost, and nothing notices when a new SQL Server version changes the
  behaviour.

**Reason.** A soundness rule is a claim about another program, and the only evidence for it is that program's behaviour.
Kept as a test, an observation is repeated on every change, and can be run against a new SQL Server version. The first
differential run corrected one of haruspex's own rules, about nested objects under `INCLUDE_NULL_VALUES`, which is the case
for doing it.

**See also:** [the guide, "Observing the engine"](../internals/09-observing-the-engine.md);
[`tests/Haruspex.DifferentialTests/README.md`](../../tests/Haruspex.DifferentialTests/README.md).
