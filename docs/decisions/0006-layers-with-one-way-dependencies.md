# Layers, with dependencies one way and checked by a test

**Date:** 9 Nov 2026

**Decision.** The code is split into layers, one project each: Diagnostics, Model, Sources, Semantics, Inference, Ir,
Config, the emitters and the CLI. A layer refers only to the layers its row of the table allows, and
`ArchitectureTests` fails on any other reference. The split was made without changing behaviour: every snapshot and CLI
output stayed byte-identical.

**Context.** The SQL Server code began as one project holding reading, the catalogue, binding and inference. Inference was
about to grow a great deal.

**Alternatives.**
- *One project*, kept tidy by convention. Dependency direction erodes one convenient reference at a time, and nothing would
  notice.
- *Split later.* The split is cheaper before the code it divides grows, and the snapshot and differential tests made it a
  safe refactor then.

**Reason.** Each layer can be tested alone, reused alone, and published alone where it is useful to others: Model and
Sources read a SQL Server project without DacFx, which linters and diff tools need too, and Semantics binds names, which
every analysis needs. Emitters see only the IR, so the IR must carry whatever they need.

**In the code:** [docs/architecture.md](../architecture.md), which holds the table, the reasons for its lines and the types
that were split; `tests/Haruspex.Tests/Architecture/ArchitectureTests.cs`.
