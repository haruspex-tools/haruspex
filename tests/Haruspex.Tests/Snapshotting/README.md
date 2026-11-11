# Snapshot helper

The snapshot tests compare haruspex's output with committed `*.verified.*` files in `../Snapshots`. This
small helper does that instead of a snapshot library.

**Why not Verify.** Verify releases after 1 September 2026 are under the Open Source Maintenance Fee terms.
The exemption for open-source projects lasts 12 months, so it would be a dated manual renewal on the main
build path, and the fee is ambiguous for contributors whose employers generate revenue. The output of haruspex
is deterministic text by design, so the core loop is all it needs: compare, write what was received, show
a diff.

**How it behaves** (`SnapshotStore.cs`):

- Line endings are normalised to LF before comparing.
- A match passes, and deletes any stale `*.received.*` file.
- A mismatch, or a missing verified file, writes `<name>.received.<ext>` beside it and fails with a unified
  diff. Review the received file against what SQL Server really does, then rename it to `.verified.`.
- `HARUSPEX_ACCEPT=1` accepts instead: it overwrites the verified file and passes. Use it only after
  reviewing, and review the resulting `git diff`.
- In CI (`CI` or `GITHUB_ACTIONS` set) it never writes a file and never accepts.
