# Generated files pass typescript-eslint's strict rules, with no disable header

**Date:** 17 Nov 2026

**Decision.** The modules haruspex writes pass ESLint's recommended rules, typescript-eslint's `strict` and `stylistic` sets,
and the two rules a team's config commonly adds to them: `method-signature-style`, so a type's functions are properties
(`request: () => sql.Request`), and `explicit-function-return-type`, so every helper says what it returns. They carry no
`/* eslint-disable */` header. The tests lint every fixture's modules with exactly these rules, any warning failing.

**Context.** A real application's trial of the npm preview, 17 Nov 2026, ran typescript-eslint's strict rules over the
generated files and found two kinds of error: methods in the `Queryable` and Standard Schema interfaces, and no return type
on the Zod module's helpers. The tests then held the files to the recommended rules alone.

**Alternatives.**
- *An `/* eslint-disable */` header.* It hides every rule, so a defect a linter would have caught in generated code goes
  unseen, by the team and by haruspex's own tests; and a config that reports unused directives
  (`reportUnusedDisableDirectives`) then reports the header itself. Teams that don't want to lint generated files already
  list them in their config's `ignores`.
- *Leave them to the team's ignore list.* Teams that lint everything they commit, as the trial's did, meet the errors first.
- *The type-checked sets* (`strict-type-checked`). They need the project's types, and the lint package installs neither Zod
  nor mssql, so every value from them would read as an error. Not ruled out; it would need those packages beside the lint.

**Reason.** Each change makes the output no worse to read: a function property says the same as a method, and the return
types are short (`z.ZodString`, `string`, `(value: unknown) => unknown`). Under `strictFunctionTypes` a property's
parameters are checked more strictly than a method's, and neither interface's functions take a parameter an implementation
could narrow, so every pool, transaction and validator that fitted before still fits. Nothing generated asks a team to change
its linter.

**In the code:** `MssqlPrinter`'s prelude and `ZodPrinter`'s helpers; `tests/emit-lint/eslint.config.mjs`, run by
`EmitCheckTests`.
