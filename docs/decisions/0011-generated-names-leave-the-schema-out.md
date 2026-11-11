# Generated names leave the schema out, and a clash is an error

**Date:** 9 Nov 2026

**Decision.** A generated name is made from the object's own name (`sales.get_order` gives `GetOrder`), with the schema's
`prefix` if it has one. When two objects' names come out the same, it is an error naming both, which asks for a `prefix` or
a `name` override. Nothing is renamed to break a clash.

**Context.** Before the config file, the schema was added to a name only on a clash, and a number broke any clash left.

**Alternatives.**
- *Always include the schema* (`DboGetOrder`). Most projects use one schema or a few with distinct names, so it is noise
  everywhere.
- *Break clashes automatically*, as before. Adding one procedure could rename another, and break code that imported it.

**Reason.** A name must depend only on its own object and the settings, never on which other objects exist, so that adding
an object can't change an existing name. Inside one procedure's types a number is still added, because it depends on
nothing but that procedure.

**In the code:** `NamingRules`, `NameSettler`; [docs/configuration.md](../configuration.md).
