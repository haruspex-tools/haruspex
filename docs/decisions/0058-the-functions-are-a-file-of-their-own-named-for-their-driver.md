# The functions are a file of their own, named for their driver

**Date:** 16 Nov 2026

**Decision.** The database module is two files. `haruspex.zod.ts` holds the schemas and types, and `haruspex.mssql.ts` the
functions, one for each procedure, which import them. `"emit": ["zod", "mssql"]` asks for both: the entry is named for the
driver the functions use, leaving room for others, and `"mssql"` needs `"zod"` beside it. `"serverOnly": true` starts the
functions' file with `import 'server-only'`; it is off by default. The functions are written by a layer of their own,
`Haruspex.Emit.Mssql`, which knows the IR and the names a schema module declares, not the Zod emitter.

Every function returns one shape: `result` for one result set, `result1`, `result2` and so on for several, `output` for the
`OUTPUT` parameters, and `returnValue`, the return code. It takes anything with `request()` first, and the parameters' object
second when the procedure has parameters. It checks values through Standard Schema, never through Zod's own methods.

**Context.** The maintainer, 16 Nov 2026: the module runs on the server (route handlers, server actions, server components in a
Next.js application), so "client" would mislead; it is "the database module". Its types and schemas are useful in the
browser too, and its functions must never be there: they take a connection, which holds the database's credentials.

**Alternatives.**
- *The functions inside the Zod module.* Browser code that imports a type would import the functions and the driver with it,
  and a validator other than Zod would need its own copy of every function.
- *An emit entry named "client" or "functions".* It names neither where they run nor what they need; a second driver would
  need a second entry anyway.
- *`server-only` always.* It is a Next.js convention, which haruspex mustn't assume.
- *A function that returns its one result set bare*, and an object only when there is more. Adding an `OUTPUT` parameter or a
  result set to a procedure would then change how every caller reads the result it already had.
- *The functions in the Zod emitter's layer.* They would know Zod, which Roadmap 6 rules out: the client rests on Standard
  Schema, so a second schema emitter serves the same functions.

**Reason.** The browser gets what it may have, the server what it needs, and nothing assumes a framework or a validator. One
shape of return means a procedure can grow without breaking its callers.

**Amended** (16 Nov 2026, [decision 0060](0060-a-call-returns-its-one-result-itself.md); 18 Nov 2026,
[decision 0070](0070-a-procedure-s-result-sets-are-named-all-or-none.md)). The one shape is no longer every function's: one
with a single result set and nothing else returns that set itself, and one whose sets the config file names (`sets`, or a
query file's `-- @sets`) returns each under its name in place of `result` or `result1`, `result2`, always as an object, so a
set added and named later is one more key.

**In the code:** `MssqlEmitter`, `SchemaModuleNames`; the CLI's `HaruspexCli.MssqlFileName`; the config's `Emitter.Mssql` and
`HaruspexConfig.ServerOnly`.
