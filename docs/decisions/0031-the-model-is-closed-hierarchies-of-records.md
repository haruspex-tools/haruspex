# The model is closed hierarchies of records, read from ScriptDom in one place

**Date:** 13 Nov 2026

**Decision.** The model's objects are records. What is one of several kinds is a closed hierarchy: an abstract record whose
constructor only its own assembly can call, with a sealed record for each kind. `SchemaObject` (a name and a `SourceSpan`) is
over `Table`, `UserType`, `Synonym` and `Module`; `Module` is over `Procedure`, `Function`, `View` and `Trigger`; `Function` is
a `ScalarFunction`, an `InlineFunction` or a `TableFunction`; a `UserType` is an `AliasType` or a `ClrType`; and what a module
runs, its `ModuleBody`, is a `SqlBody`, a `ClrBody` or a `NoBody`. ScriptDom's types appear in the model only as bodies and
expressions (statements, a query, a scalar or Boolean expression), which an architecture test holds. Reading ScriptDom into the
model is in one layer, Haruspex.SqlServer.Syntax (`SyntaxReader`, `ModuleReader`), which Sources and the layers that bind and
infer share. Every collection of schema objects is in source order.

**Context.** One record stood for views, synonyms and functions, with a kind and three fields (a view's definition, its file,
a synonym's target), each null unless the kind was the one it belonged to. A function's kind was read from ScriptDom's return
type at every use, a CLR body from a null statement list, and a parameter from ScriptDom's declaration, so every reader of the
model needed ScriptDom to know what an object was. The model also held the code that read a name, a type and a table
definition from ScriptDom, so its public surface was ScriptDom's.

**Alternatives.**
- *A kind and nullable fields* (what that one record was). The compiler can't tell which fields a kind has, so each reader checks
  the kind and then asserts the field is set, and a field added for one kind is null, silently, for the others.
- *A discriminated-union library* (OneOf and its kind). It adds a dependency for what C# says with records and pattern matching,
  and its cases are positions (`T0`, `T1`) rather than names in the domain's words.
- *Open hierarchies* (a public constructor). Anyone could add a kind, so a `switch` over the kinds could never be known to be
  complete. With the constructor closed, the kinds in the assembly are all there are.
- *Synonym under `Module`*, as first planned. SQL Server keeps a definition for its modules (`sys.sql_modules`: procedures,
  functions, views and triggers), and a synonym has none, only a name it stands for. Calling it a module would put a wrong word
  in the domain's vocabulary; it is a `SchemaObject` beside the modules.
- *A computed column as a kind of its own.* Everything a reader asks of a column (its name, nullability, collation, whether
  `SELECT *` shows it) is asked of both kinds, and only where its type comes from differs, so `Column` stays one record whose
  `Column.Type` is null when its `Column.ComputedExpression` is set.
- *A table's declaration apart from the table*, so that a body's `#t` and `@t` and a function's return table, which are not
  schema objects, need not be `Table`s named with no schema. The schema's rules would then take a declaration, and the name
  their facts are given in the IR would have to come with it: a check's fact is named by its schema for a table of the schema
  (`sales.ck_status`) and by its table for a body's (`#totals.ck_amount`), and a message about a body's column names its
  table. Binding a body's own tables as declared is Semantics' work, which it doesn't do yet, and the declaration belongs with
  that change; until then such a table is a `Table` with an empty schema, never in `DatabaseModel`, as `SchemaObject` says.
- *The reader in Model.* Anything public in Model's assembly is the model's public surface, and binding needs the reader too
  (a body's `CREATE TABLE #t` is read as the schema's is). In Sources, binding would depend on reading files. So it is a layer
  of its own between them.
- *Names in alphabetical order.* An order by name is the order of a listing, which a reader that wants one sorts for itself; the
  model's own order is where its objects are, which also orders the definitions of one name, as triggers have.

**Reason.** A reader matches on a kind and gets exactly the fields that kind has, named in the domain's words, and the compiler
checks the rest. ScriptDom stays where only syntax can say something (a body's statements and expressions), so a tool that reads
the model needs ScriptDom only to read code. One order, set in one place (`DatabaseModel`), makes the model the same however its
files are listed. Records give the model immutability and `with`; their equality is the generated one, so a record that holds a
list or a ScriptDom fragment compares it by reference, and an object's identity is its name (`SqlType`, a value, compares its
arguments item by item).

**Amended 13 Nov 2026, again.** The alternative of a table's declaration apart from the table was taken when binding came to
bind a body's own tables: a `#t`, an `@t` and a function's return table are each a `DeclaredTable`, named by the body with its
sigil and in no schema, and the schema's rules read a `Table` or a `DeclaredTable` alike through `ITable`. The IR names a
check's fact by which it is, and a message about a body's column names its table as the body writes it.

**Amended 13 Nov 2026.** "The compiler checks the rest" overstated it. The compiler checks a reader's fields, but not that a
`switch` over the kinds handles each: it can't know a hierarchy is closed, so every such switch needed a discard arm, and a
kind added later failed only at run time. Decision [0032](0032-a-switch-over-a-closed-hierarchy-is-exhaustive.md) closes the
gap with an analyser, and the discard arms are gone.

**See also:** [the guide, "The inputs and the model"](../internals/README.md); `docs/architecture.md`, on the Syntax layer.
