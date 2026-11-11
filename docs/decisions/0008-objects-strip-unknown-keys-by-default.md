# Objects strip unknown keys by default

**Date:** 9 Nov 2026

**Decision.** A generated object schema strips keys it doesn't know (`z.object` in Zod), so an application never sees a
field it was not generated for. `"objects": "strict"` in `haruspex.json` makes it reject them (`z.strictObject`). This
applies to results only.

**Context.** `FOR JSON` produces exactly the keys haruspex knows, so a strict object is the accurate schema. But in most
teams the database is deployed before the applications that call it.

**Alternatives.**
- *Strict by default.* It was the first default, and was reversed the same day: with the database deployed first, adding a
  column to a result would make every running application reject every payload until it was redeployed.

**Reason.** The types stay exact; only the reaction to an unexpected key changes. This is the one deliberate exception to
[claiming everything the engine guarantees](0005-claim-everything-the-engine-guarantees.md), made for deployment safety. A
project that deploys its applications first, or together with the database, can choose strict.

**In the code:** `ZodObjects`, and `ZodEmitter`, where each object is written.
