# Results that cannot be inferred are declared, and still checked

**Date:** 9 Nov 2026

**Decision.** Where a procedure's results cannot be known from the source (dynamic SQL, `EXEC @name`, a CLR procedure), the
config file can declare them, as a JSON Schema per result set or as `none`. A declaration is a claim a person makes: the
generated code still checks it at runtime, it flows into the results of every procedure that calls the declared one, and a
declaration of results haruspex can infer is compared with them.

**Context.** Some procedures build their query as a string. Their shape is in no source file, and SQL Server can't describe
it either.

**Alternatives.**
- *Leave such procedures unknown.* Honest, but a caller of one would be unknown too, all the way up.
- *Parse the strings.* Possible only where the string is built from literals, which is rarely the case where it matters.

**Reason.** The person who wrote the dynamic SQL knows its shape. Stating it once, in a place that is checked, is better than
each caller assuming it. Because the generated validators check every value, a wrong declaration fails loudly rather than
passing as a silent cast.

**In the code:** `DeclaredSchema`, `ResultsComparison`, `DeclaredResults`; [docs/configuration.md](../configuration.md).
