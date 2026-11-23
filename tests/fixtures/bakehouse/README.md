# Fixture: bakehouse

An invented bakery chain for a union whose variants differ only below their top level: two branches return JSON objects of
the same keys, and only their nested `FOR JSON` lists differ. `z.union` takes the first variant that accepts a value and a
`z.object` strips the keys it doesn't know, so unless each variant refuses the keys only the other's nested objects have, the
first variant would take the second's objects and drop their nested keys. No part of it comes from a real system. The seed
(`tests/fixtures/bakehouse/seed.sql`) gives a shop with two sales, one with one and one with none; a run of each procedure
deletes every shop, so the result is seen null.

| Procedure | Covers |
|---|---|
| `bake.brief_or_full` | the brief branch first (each sale's id), then the full one (its id and loaves): the brief variant refuses `loaves` in its nested sales, so a full object reaches the full variant and keeps them |
| `bake.full_or_brief` | the same branches the other way round, so that neither order loses a key |
