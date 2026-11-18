# A mapper accounts for every key of a generated row

**Date:** 17 Nov 2026

**Decision.** `"mapping"` in `"emit"` writes `haruspex.mapping.ts`, which exports one type, `Exhaustive<Row, Account>`. The
account is a type literal with an entry for every key of the row: `'used'`, or `'ignored: '` followed by the reason, or, for a
key that holds objects (itself, in a list, or when it may be null or absent), an account of those objects. The type is the row
without the keys the account ignores, at every depth, so the row a generated function returns is one, and a mapper that takes
it can't read a key it said it ignores:

```ts
type PlantRow = Exhaustive<GetPlantRow, {
  id: 'used'
  name: 'used'
  kind: 'ignored: the catalogue reads it, not this screen'
  water_log: { watered_at: 'used'; litres: 'used' }
}>
```

The account is refused, as a compile error at the account, when it leaves a key out at any depth, names a key the row doesn't
have, marks a key that holds objects `'used'` whole, ignores a key with no reason (or only spaces), or gives a key a union of
decisions (`'used' | 'ignored: maybe'`), which would let either rule be dodged. A record, whose keys are data, is accounted for
by its values: `'used'` when they are values, their own account when they are objects. An object type that holds itself has an
account that does too (`interface NodeAccount { …; children: NodeAccount }`), and a union of objects has an entry for every
key of every variant. A `Date` is a value, not an object with keys. The file is types only, the same for every module, and needs
`"zod"` beside it.

**Context.** An application's own types differ from the generated rows by decisions the database can't make: an absent list
that is empty to the application, a figure that is money, a name the application prefers. Those decisions are made in a
mapper, written by hand. When the procedure gains a column, the regenerated row has a key the mapper has never decided about,
and a mapper that builds its object from the keys it names compiles as before: the column is dropped without anyone choosing
to drop it. A trial in a real application wanted the compiler to stop there, at any depth, in the one place that must decide. The stop
comes when the module is regenerated, not when the database changes: until then the schema drops the new key (or, under
`"objects": "strict"`, refuses it at run time), and what notices the module is out of date is `haruspex check` in CI.

**Alternatives.**
- *A function the mapper calls*, such as `exhaustive(row, { … })`, checking at runtime. It costs at every call, and a check
  at runtime can't fail the build, which is when the decision is wanted.
- *Destructuring, with the rest of the row required empty* (`const { id, name, ...rest } = row`, then `rest` checked). It
  sees which keys are read, which the account can't, but it takes a statement per object, does nothing at depth unless every
  nested object is destructured too, and leaves the reason for ignoring a key nowhere.
- *`true` for a key used*, or a named type for each decision. A string says what it means where it is read, and a key ignored
  carries its reason in the same place.
- *`'used'` allowed for a key that holds objects.* A key added inside them would then reach the application unseen, which is
  what the account is for. Such a key is used through its own account, or ignored whole.
- *Always written*, as part of the Zod module or as a file beside it. The Zod module states claims about the database, line
  by line; the helper claims nothing, and is for the application's code. A file written for every module would sit unused in
  each application that doesn't map, and `check` would compare it there too. Asking for it costs one word.
- *A setting* (`"mapping": true`). `"emit"` is where each file `generate` writes is named.
- *An npm package.* An install and a version to keep in step with the generated code, for a few lines of types.
- *Not needing `"zod"`.* The helper works on the types the Zod module declares, and a file that may later hold code generated
  from the same IR would import them. Allowing it alone later breaks no config file; requiring `"zod"` later would.

**What it can't check.** Whether a key marked `'used'` is read: the compiler sees the type a function takes, not what its
body does. A mapper that takes its keys by destructuring lets typescript-eslint's rule against unused variables find a key named and
never read. Nor whether a key became optional or nullable: its entry is still `'used'`, and the mapper's body stops compiling
only where it relies on the value.

**Reason.** The decision about a new column is made where the application's types are made, and the build can't pass until
it is. It costs nothing at runtime, and nothing in an application that doesn't ask for it.

**In the code:** `MappingModule`; the config's `Emitter.Mapping`; the CLI's `HaruspexCli.MappingFileName`;
`tests/emit-check/src/mapping-usage.ts`, which compiles accounts of the fixtures' rows that must and must not type-check.
