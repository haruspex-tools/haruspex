# Fixture: tannery

An invented tannery's vats, for canonical value sets: each string column has a CHECK set under a different collation, or none,
and its `haruspex.json` declares the database's default collation, which a folder of scripts doesn't record
([decision 0043](../../../docs/decisions/0043-a-value-set-is-canonical-by-a-fold-observed-from-the-server.md)). No part of it
comes from a real system. The seed (`tests/fixtures/tannery/seed.sql`) stores values that equal a member without being it:
another case, trailing spaces, an accent the collation ignores, a soft hyphen it ignores. The differential tests parse each
run's output with the generated Zod, so each is shown to map to its member.

| Procedure | Covers |
|---|---|
| `dye.list_vats` | Canonical sets under the declared default (`state`), `Latin1_General_100_CI_AI` in a `char(4)` (`shade`), `Latin1_General_100_CS_AS` with members differing only in case (`mode`) and `Latin1_General_100_CI_AS` with a soft hyphen (`batch`); notes for `nvarchar`, a collation not proven, a member that isn't plain, and two members that fold alike |
| `dye.vat_card` | Canonical sets inside `FOR JSON` |
| `dye.states` | A `UNION` of two canonical sets under one fold |
