# Configuration: `haruspex.json`

A project's settings live in `haruspex.json`. `haruspex generate` looks for it in the working directory and then in
each folder above it, or uses the file `--config` names. Paths in it are relative to the file. It is JSON with
comments and trailing commas, as `tsconfig.json` is. Its JSON Schema is published at
[`schemas/haruspex.schema.json`](../schemas/haruspex.schema.json); name it in `$schema` and editors complete and check
the file as you type.

**An unknown key is an error**, with its path and line. It is never ignored, because a misspelt setting would
otherwise silently do nothing.

```jsonc
{
  "$schema": "https://github.com/sharpblue/haruspex/raw/main/schemas/haruspex.schema.json",
  "source": "database",                     // the folder of .sql files
  "out": "src/db",                          // where haruspex.ir.json and haruspex.zod.ts go

  "objects": "strip",                       // or "strict"

  "naming": {                               // for every schema
    "replace": [{ "pattern": "#$", "with": "Row" }],
    "case": "pascal"                        // or "camel", "preserve"
  },

  "schemas": {                              // keyed by the schema's name as it is
    "sales": { "include": ["get_*", "list_*"], "exclude": ["*_internal"] },
    "audit": { "include": ["*"], "prefix": "Audit" },                          // AuditGetOrder
    "RDF.": { "include": ["GetPropertyList"], "naming": { "case": "preserve" } }
  },

  "procedures": {
    "sales.get_order": {
      "name": "OrderDetail",
      "paths": {
        "lines[]": { "name": "OrderLine" },
        "customer.email": { "nullable": false, "why": "checkout requires an email" },
        "status": { "values": "exact", "why": "the application writes lower case only" },
        "settings": { "type": "OrderSettingsSchema", "import": "@app/orders/settings" }
      }
    }
  }
}
```

The command line overrides the file for one run: the folder argument replaces `source`, `--out` replaces `out`, and
`--include schema.pattern` replaces the schemas' includes (each schema's `exclude` still applies, and overrides of
procedures the run leaves out are skipped). Without a file, `--include` and `--out` are required.

## Which procedures are typed: `schemas`

Settings are per schema. Each key of `schemas` is a schema's name exactly as in the database: no brackets, dots
allowed (`"RDF."`). Its block holds:

- `include` and `exclude`: patterns matching the schema's procedures by their **own names**, ignoring case. `*` matches
  any characters, `?` one; brackets make what they hold literal (`[a*b]`). `exclude` wins over `include`.
- `prefix`: put in front of the names of the schema's objects (see Naming). Empty by default.
- `naming`: the schema's own naming rules, merged over the top-level ones: its `case` replaces the top-level case, and
  its `replace` rules run after the top-level ones.

**Nothing is selected unless its schema has a block with `include`**: typing every procedure is something you ask for
(`"include": ["*"]` per schema), never the default. Whatever matches nothing is an error: a schema key that names no
schema of the project, an include that selects nothing in its schema (a typo would otherwise drop an interface without
anyone noticing), and an exclude that removes nothing its include selected.

A block is an object so that settings can be added to it later (one output module per schema is planned) without
breaking a file written today.

## Overrides

An override corrects or completes what was inferred, for one procedure (keyed by its two-part name, which must be
selected). Overrides are applied to the IR after inference, never inside it, and every one stays **checked at
runtime**: the generated validator still checks every value, so a wrong override fails loudly on real data; it is never
a silent cast. The IR lists every claim an override made under `overrides`, with its reason.

- `name`: the procedure's generated name.
- `paths`: places in its results, in the IR's path syntax, relative to the result: `[]` for a list's rows, `.key` (the
  first dot may be left out) or `["a key"]`, `{}` for a record's values, `[0]` for a tuple's position, `<json>` for
  what JSON text holds. A procedure with several result sets names one: `results[1][].id`. Each path takes:
  - `name`: the name of the object type there. Types inside it build their names on it.
  - `nullable: false` with a required `why`: the value is never null, which the database doesn't prove. A key `FOR
    JSON` leaves out when null then becomes required.
  - `type` and `import`: the value is the application's own schema, `type` exported by the module `import`, with
    `[]` for each level of array (`"TagSchema[]"`). Any unknowns there are then declared, not reported. A relative
    `import` (`./`, `../`) is relative to this file, and is rewritten relative to `out`; a package or alias is kept as
    written.
  - `values: "exact"` with a required `why`: a value set that the database compares under its collation (where `'Open'`
    can pass a check for `'open'`) holds exactly its items, giving a `z.enum`. It needs a value set the IR already has,
    from a CHECK constraint on a column the value passes through unchanged.

**An override whose path doesn't exist is an error**, naming the keys that do. **One the inference already agrees
with is a warning**, so the file shrinks as the SQL improves. Generation goes ahead after a warning.

## Results policy: `objects`

- `"strip"`, the default: `z.object`. Types are still exact, but keys a type doesn't list are removed when parsing, so
  a column the database gains before the application redeploys doesn't break the running application.
- `"strict"`: `z.strictObject`, for projects that deploy the application first or together with the database, and want
  an unexpected key to fail loudly.

This is the one deliberate exception to "claim everything it does guarantee": `FOR JSON` produces exactly the known
keys, so strict is the accurate schema, and strip is the default for deployment safety. It applies to results only.

## Naming

Generated names come from each object's own name, **never its schema**, by declarative rules applied in this order:

1. `replace`: regular expressions over the name, in order, each replacing every match (`$1` names a group); the
   top-level rules first, then the schema's own. `#$` is the end of the name. Expressions run without backtracking, so
   backreferences and lookarounds are not available.
2. `prefix`: the schema's prefix, if any, goes in front as words of its own: `"prefix": "Audit"` makes
   `audit.get_order` `AuditGetOrder`.
3. `case` (the schema's, or else the top level's): `pascal` (the default) breaks words at every character that isn't a
   letter or digit (`$`, `#`, `_`), so `orders$pipeline$velocity` gives `OrdersPipelineVelocity`. `camel` gives
   `ordersPipelineVelocity`. `preserve` keeps the prefix and the name as they are, `$` included; anything that is still
   not a valid identifier is an error naming the object.

Nested types build on their procedure's name: `GetOrderRow` for its rows, `GetOrderLine` for the items of `lines`,
`GetOrderResult2Row` for a second result set's rows, and `PartTreeNode` for the recursive type of a function
`bom.part_tree` (named by its own schema's settings).

**Names don't shift.** When two objects' names come out the same (two schemas with a `get_order` each, say), it is an
error naming both, which asks for a `prefix` on one of the schemas or a `name` override; haruspex never adds a number
between objects, because that renumbered names whenever an object was added and broke code that used them. Inside one
procedure two types can still meet (a nested key `row` beside the row type, or the variants of a union, which share a
path); there a number is added in the order the procedure's results are written (`GetOrderRow2`), because it depends
on nothing but that procedure.

## Errors

Every problem is written to standard error as a compiler writes one, so editors and CI can jump to it:

```text
haruspex.json:14:9: error: procedures["sales.get_order"].paths["lines[].skew"]: the path results[0].lines[].skew does not exist: the object at results[0].lines[] has no key "skew". Its keys are "sku", "quantity".
haruspex.json:22:7: warning: procedures["sales.get_order"].paths.id: "nullable": false agrees with the inference, which already proves the value at results[0].id is never null. Remove it.
haruspex.json: error: "GetOrder" would name both the procedure audit.get_order and the procedure sales.get_order. Generated names must not clash: give one of their schemas a "prefix" in "schemas", …
```

Errors exit with code 2, as command line mistakes do; nothing is written.
