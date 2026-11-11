# Parameter objects are strict

**Date:** 9 Nov 2026

**Decision.** Where haruspex generates a schema for what a client *sends* (a procedure's parameters as one object), that
object is strict: a key it doesn't know is an error, whatever the `objects` setting says for results.

**Context.** [Results strip unknown keys](0008-objects-strip-unknown-keys-by-default.md) because the database may add a field
before the application is redeployed. No generated code checks parameters yet; this record states the rule it will
follow.

**Alternatives.**
- *The same setting for both.* The reason for stripping doesn't apply to parameters, and a stripped parameter object would
  drop a misspelt key without a word, so the procedure would run with that parameter's default.

**Reason.** What the client sends is under its own control, so an unknown key there is always a mistake in the client, and
should fail before the call is made.
