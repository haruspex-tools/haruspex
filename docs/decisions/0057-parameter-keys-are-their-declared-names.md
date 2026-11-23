# Parameter keys are their declared names

**Date:** 16 Nov 2026

**Decision.** In the parameters' type, the `OUTPUT` parameters' type and a function's arguments, a parameter's key is its
declared name without its `@`, unchanged: `@basket_id` is `basket_id`, so a call is `ringUp(db, { basket_id })`. The maintainer,
16 Nov 2026: "Declared names".

**Context.** A result's keys are its columns' names as SQL Server sends them, and the generated schemas keep them. The design
sketched camel case for parameters (`@person_id` as `personId`), which would make one call's arguments and its results follow
two rules.

**Alternatives.**
- *Camel case, by a fixed rule.* Parameters would be renamed and results not, and a name such as `@CustomerID` has no camel
  case everyone agrees on.
- *Camel case by the config's naming rules.* The rules name procedures and types; keys have none yet.

**Reason.** One rule for every key in the module, which needs no setting. The key-renaming rules planned for keys inside JSON
trees will be one setting for keys in both directions, parameters and results together, so a team that wants camel
case gets it everywhere at once.

**In the code:** `SchemaModuleNames.Key`, and `ParameterLowering.Key` in the Zod emitter, which declares the same keys.
