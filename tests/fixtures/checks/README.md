# Fixture: checks

Invented tickets, owners and events whose columns carry every fact haruspex reads from a schema: alias types
(`chk.short_code`, NOT NULL; `chk.pct`, NULL), CHECK constraints giving value sets (`IN`, OR-ed equalities),
ranges (`BETWEEN`, comparisons combined by AND, inclusive and exclusive), and minimum lengths (`len(…) > 0`,
`<> ''`), and string lengths in a single-byte and a UTF-8 collation, and a set under a binary collation (`sigil`), exact but
for trailing spaces. Some constraints are there to be ignored:
`ck_ticket_pair` (two columns), `ck_ticket_replicated` (NOT FOR REPLICATION), `ck_ticket_muted`, which
`ticket_nocheck.sql` disables, and `ck_ticket_late`, which `ticket_late.sql` adds WITH NOCHECK after a row that
breaks it. `ck_ticket_restored` is disabled and then validated again (`ticket_restored.sql`), so it is read. The
seed and `ticket_late.sql` write rows that break the two untrusted constraints, so a claim from either would fail. The seed (`tests/fixtures/checks/seed.sql`) reaches every edge:
each bound, each value of each set, each maximum and minimum length, and null in every nullable column. It also
stores values that pass a string CHECK without being one of its strings exactly (`'CLOSED'`, `'open      '`,
`'sw'`), which is why a set of strings is never emitted as an enum.

| Procedure | Covers |
|---|---|
| `chk.list_tickets` | every fact through `FOR JSON PATH`: aliases, a left join, a nested object, a nested subquery |
| `chk.ticket_rows` | the same columns as a rowset |
