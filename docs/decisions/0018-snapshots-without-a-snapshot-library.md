# Snapshots without a snapshot library

**Date:** 9 Nov 2026

**Decision.** The snapshot tests use a small helper in the repository (`SnapshotStore`), not a snapshot library.

**Context.** Verify is the usual snapshot library for .NET. Its releases after 1 September 2026 are under the Open Source
Maintenance Fee terms.

**Alternatives.**
- *Verify.* The exemption for open-source projects lasts 12 months, so it would mean a dated renewal on the main build path,
  and the fee is ambiguous for contributors whose employers earn revenue.
- *Verify at an older version*, frozen. It would miss fixes, and still need moving off later.

**Reason.** The output of haruspex is deterministic text by design, so the core loop is all it needs: compare, write what was
received, show a diff. That is under 200 lines, with no terms attached.

**In the code:** [`tests/Haruspex.Tests/Snapshotting/README.md`](../../tests/Haruspex.Tests/Snapshotting/README.md).
