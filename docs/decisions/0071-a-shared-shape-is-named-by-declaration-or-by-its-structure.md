# A shared shape is named by declaration or by its structure

**Date:** 18 Nov 2026

**Decision.** In the Zod module, every exported name is its own procedure's type: `AdminStockProductsRow` is exported as
`AdminStockProductsRowSchema` and `AdminStockProductsRow` whatever other types share its shape. A shape several named types share is still
declared once, and each of them is exported as that one schema. It is declared under a name the config file gave one of them
(a `name` in `paths`, the ordinally first when several were), as the shop fixture names `PriceListItem` once; otherwise it is a
constant the module doesn't export, named `shape` and the first six hexadecimal digits of the SHA-256 of its schema as the module
writes it (`shape3f2a9c`), taking more digits only when another shape of the module, or an imported name, already has them. The
IR records which names the config file gave (`nameDeclared`, 0.23). The JSON Schema document is unchanged: each named type is
written in full in `$defs`, under its own name. Richard accepted the design on 18 Nov 2026.

**Context.** Identical shapes were declared once under the first type in the module's order, and the rest were aliases of it:
a trial of the npm preview on a real application found `OrdersResult2RowSchema = InvoicesResult3RowSchema` for a one-row
`{ total }`, which reads oddly, and names one procedure's type after another's, so adding or removing a procedure could change
what another's alias pointed at. On that application's corpus there were 561 aliases, one shape shared 123 times; writing each
out in full would have added about 155 KB to a 582 KB module.

**Alternatives.**
- *Keep the first procedure's name*, with the procedures sorted so that "first" is stable. The smallest change, but one
  procedure's names still read as another's, and adding a procedure before it renames the declaration.
- *Write every shape out in full*, as JSON Schema does. The clearest, but 27 % larger on that corpus.
- *A name made from the types that share it*, such as the first in order. That name changes whenever a procedure sharing the
  shape is added or removed; a hash of the structure changes only when the shape does.
- *In JSON Schema, a shared definition in `$defs` with each type a `$ref` to it.* Every definition in `$defs` can be referred to,
  so there is no unexported name to hide a hash behind, and each type's definition says where it comes from and which unknowns
  are at its place, so two types of one Zod shape are rarely one definition there.

**Reason.** An application imports only names it can read as its own: its procedure's types, or a name it declared. The
unexported constant is the module's own business, so its name can be one that is stable whatever else the module holds: it is
a hash of the schema's text, in which a shape inside it appears by its own name, so it depends on the shape alone. A collision
of six digits between two shapes of one module is rare, and gives the later one more digits rather than one name to two shapes.
Shared declarations are where a module of one schema per file will put a common file, so they are named by what they are, not
by who used them first.

**In the code:** `ZodLowering.SharedShapes` and `ZodLowering.Named`, `ShapeNames`, `IrObjectType.NameDeclared` and `NameSettler`.
