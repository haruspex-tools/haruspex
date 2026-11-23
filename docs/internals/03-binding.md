# Binding

A name in a body means something only in a place. `name` in a select list is a column of whichever table in reach has one;
`sale` in a FROM clause may be a table of the schema or a CTE the statement defined a line above; `@n` is a parameter or a
variable. Working out what each name means is **binding**, and it comes before anything about values: Inference can't say
what a column holds until it knows which column it is. This chapter is about that step: the scopes a query's names are
looked up in, SQL Server's rules as haruspex models them, why binding is a layer of its own, and how Inference reads what it
gives. The code is [Semantics](../../src/Haruspex.SqlServer.Semantics/README.md), and its `Binder`.

## The idea: symbol tables, scopes and shadowing

A compiler calls this *name resolution*, part of *semantic analysis*. The structure it uses is a **symbol table**: a map
from a name to what it means (its *symbol*). A program has many of them, one for each **scope**, a region of code where a set
of names is in force, and they are chained: a block's table points to the enclosing block's, so a name not found in the
inner one is looked for outward. A name declared in an inner scope **shadows** one of the same name outside it, which is then
out of reach there. Appel's *Modern Compiler Implementation* calls a chain of immutable tables a *functional* environment:
entering a scope makes a new table on top of the old one, and leaving it means going back to the old one, which nothing
changed.

SQL has exactly this shape. Each query specification (`SELECT … FROM … WHERE …`) is a scope, its FROM clause declares the
names in it (each source, by its alias or its own name), and a subquery's scope is chained to the query it is written in.
So a correlated subquery reads the outer query's columns by looking outward, as a nested block reads an outer variable.

## An example

```sql haruspex-example
CREATE TABLE shop.product
(
    product_id int          NOT NULL PRIMARY KEY,
    name       nvarchar(40) NOT NULL,
    note       nvarchar(80) NULL
);
GO
CREATE TABLE shop.sale
(
    sale_id    int  NOT NULL PRIMARY KEY,
    product_id int  NOT NULL,
    sold_on    date NOT NULL
);
GO
CREATE PROCEDURE shop.best_sellers
AS
WITH sale (product_id, sold) AS (
    SELECT s.product_id, COUNT_BIG(*)
    FROM shop.sale AS s
    GROUP BY s.product_id
)
SELECT p.name,
       sale.sold,
       (SELECT TOP (1) note FROM #picked AS k WHERE k.product_id = p.product_id) AS picked_note
FROM shop.product AS p
JOIN sale ON sale.product_id = p.product_id;
```

```ts haruspex-zod
export const BestSellersRowSchema = z.object({
  name: z.string().max(40),
  sold: z.string().regex(/^(0|-?[1-9]\d{0,18})$/).refine((value) => /^-?\d+$/.test(value) && BigInt(value) >= -9223372036854775808n && BigInt(value) <= 9223372036854775807n, { error: "Outside the range of bigint" }),
  /**
   * Not known (caller-temp-table); the IR's unknowns say why.
   * To change it: declare the results in the config file's "results".
   * Why: haruspex explain shop.best_sellers picked_note
   */
  picked_note: z.unknown(),
})
export type BestSellersRow = z.infer<typeof BestSellersRowSchema>

/** shop.best_sellers: returns rows. */
export const BestSellersSchema = z.array(BestSellersRowSchema)
export type BestSellers = z.infer<typeof BestSellersSchema>
```

Three names are bound here, and each binding decides a field:

- `sale` in the outer FROM is the CTE, not the table `shop.sale`: a one-part name means a CTE of the statement before it
  means anything in the schema. Its columns are its column list's, so `sale.sold` is the `COUNT_BIG(*)` its query leaves
  unnamed, a `bigint`, which the driver delivers as the string of its digits.
- `p.product_id` in the subquery is the outer query's: the subquery's own scope has no source called `p`, so the lookup goes
  outward.
- `note` in the subquery has no qualifier. The outer `shop.product` has a `note`, but the subquery's own source, the caller's
  `#picked`, may have one too, and SQL Server would read that one. Its columns aren't known (it is whatever the caller
  created), so `note` is taken to be its, and `picked_note` is unknown for the reason `#picked` is, rather than claimed as
  the product's note.

## The rules haruspex models

### A column

`Binder.Resolve(ColumnReferenceExpression, QueryScope)` is the one rule every column reference in a body is resolved by, and
it gives a `Resolution`:

- **A qualified column** (`p.name`) is the column of the source its qualifier names, in the innermost scope that has a source
  of that name. A qualifier shadows the same alias further out. A source without an alias is named by its table's name
  (`product.name`) or by its full name (`shop.product.name`, below).
- **An unqualified column** (`name`) is the column of the one source of the innermost scope that has a column of that name.
  When two sources of that scope have it, it is ambiguous (SQL Server's error 209), and nothing is claimed. When none is
  known to have it but a source of that scope may (its columns aren't known), it is taken to be that source's, as in the
  example: going outward past it could read the wrong table.
- **What it gives**: a `TableColumn` (a column of a table of the schema), a `RelationColumn` (of a CTE, a derived table, a
  temporary table and the rest), an `UnlistedColumn` (one of a relation whose columns aren't known), or an `Unresolved` case
  saying why: `AmbiguousColumn`, `MissingColumn`, `UnmodelledReference` (four-part names, pseudo-columns), `UnboundSource`
  (a source that can't itself be bound, its problem moved to where the column is read). Every unresolved case becomes an
  unknown with its reason in the IR, never a guess.
- **Names that differ only in case.** Names are matched ignoring case. Two columns of one source whose names differ only in
  case (`[Code]` and `[code]`) can exist only where names are compared by case, which haruspex doesn't model, so a reference to
  either, and a star's column for either, is an `AmbiguousColumn` naming both, rather than the first
  ([decision 0107](../decisions/0107-names-that-differ-only-in-case-are-unknown.md)). Two variables are a `CaseVariantVariable`
  in the same way.

### A full name

A column or star can name its source by schema and table, `shop.product.name` or `shop.product.*`, as some projects write
every column. Once bound, it is exactly what `p.name` would be: binding is all there is to it. The rule is the name a source is
**exposed** under, which `BoundSource.Exposed` records (an `ExposedName`), and each part of it was observed
(`BindingObservationTests`), with the same outcome at every compatibility level from 100 to 170:

- Only a source **without an alias** has one: an alias hides the table's own name, both `shop.product.name` and
  `product.name` (SQL Server's error 4104). A full name an inner query doesn't expose (its table has an alias, is the table of
  that name in another schema, or is a CTE or derived table of that name) is looked for outward, as any qualifier is; one it
  exposes too is the inner query's.
- A name written with its schema is exposed so. A **one-part name** in FROM is exposed under the schema it resolves to *for the
  caller*: `FROM product` exposes `dbo.product` to a caller whose default schema is `dbo`, `shop.product` to one whose
  default schema is `shop`. Which object a one-part name reads is the same question (`SchemaNames.Resolve`), so when the
  project has it in one schema only, that is the schema; when several schemas have it, which full name binds is the caller's,
  and the column is unknown with the source's reason.
- A view, a synonym and a table-valued function are exposed under their own names, a synonym not under its target's. A
  temporary table is exposed under **any** schema (`dbo.#picked.note` and `shop.#picked.note` both bind), and a CTE under none.
- Two sources of one query can't be exposed under one table name, even in two schemas: SQL Server refuses the FROM (error
  1013), so a full name that matches two is ambiguous, never the first.
- A source named with a database binds a full name only when that database is the one the query runs in. Through a
  reference the project holds it is another database, so a full name passes it by and is looked for outward, as SQL Server
  does; a database the project doesn't hold may be this one, so a column through it is unknown. A four-part name, `db.shop.product.name`, binds in the database the query runs
  in, but is unknown here: claiming less, and no corpus haruspex is measured on writes one.
- `OUTPUT` can't name the table it writes by any name of its own, even when FROM lists it without an alias (4104): only
  `inserted` and `deleted` read it. A column so named is unknown, as one through its alias in FROM already was.

Names that contain dots are bracketed, `[sales.eu].[order.line].qty`, and ScriptDom gives the parts without their brackets,
so they are compared as any other.

### A name in FROM

A one-part name is looked for in the names the body has defined first, and in the schema only after
(`NameEnvironment.Find`), in this order:

1. `inserted` and `deleted`, inside a write's `OUTPUT`.
2. **A CTE** of the statement's `WITH`, which shadows any table of its name. Its scope ends with its statement: the next
   statement's `sale` is the table again. A CTE whose query reads its own name is **recursive**, and is in scope inside its
   own query; its columns are its anchor's, the query before the first `UNION ALL`, which is bound first.
3. **`@t`**, a table variable, as its `DECLARE` declares it.
4. **`#t`**, a temporary table. SQL Server resolves a temporary table when the statement that reads it runs, not when the
   procedure is created (*deferred name resolution*), so which columns `#t` has depends on the path that reached the
   statement: whether its `CREATE TABLE` ran, what an `ALTER TABLE` did, or what the caller created. That is a fact about the
   path, which only the walk knows ([chapter 6](06-flow-joins-and-fixed-points.md)), so the walk passes each temporary table's
   columns in as part of the names in force. Which declaration `#t` means is not the path's: a procedure can create a given
   `#t` once at most (observed: `A_procedure_cannot_create_a_temporary_table_twice_even_after_dropping_it`). SQL Server
   ignores every other part of a temporary table's name, so `dbo.#t` and `tempdb..#t` are `#t` wherever they are written, a
   write's target included, and so is `srv.tempdb.dbo.#t` in a read or a write; making or dropping the table takes three parts
   at most (`Binder.Temporary`; observed: `A_temporary_table_named_with_a_schema_database_or_server_is_the_sessions_own`,
   `A_temporary_table_is_made_or_dropped_with_three_parts_at_most`).
5. **The schema**: a table, a view (read from its definition), a synonym or a function. A schema-qualified name means
   itself. A name without a schema is resolved in the caller's default schema and then in `dbo`, which the source can't
   tell, so it means the one object of that name if exactly one schema has it, and is ambiguous if more do
   (`SchemaNames.Resolve`, the one rule for every kind of name: a FROM, a write's target, an `EXEC`, a function, an alias
   type). It gives the decision itself, `OneObject`, `SeveralObjects` or `NoObject` (which then means `dbo`'s), and each user
   says only what it makes of each.

Each thing a FROM can read that is not a table of the schema is a `RelationSymbol`: a `CommonTable`, a `DerivedTable` (with its
query, bound), a `ValuesTable`, a `ViewRelation`, a `TableFunctionCall`, an `OpenJsonCall`, a `SeriesCall`, a
`TemporaryTable`, a `TableVariable` or a `PseudoTable`. A symbol carries its columns' names, which binding works out itself:
a select list's aliases and column names (a set operation's from its first query), a column list, a declaration,
`OPENJSON`'s `WITH`, and a star's columns (below).

### `SELECT *`

A star is one more name to bind: `*` stands for every source of its query, `alias.*` and `shop.product.*` for the one its
qualifier names, and
`Binder.Star` lists the columns it stands for as a column reference to each would resolve, so Inference reads a starred column
exactly as a listed one, with its type, its facts and its optional side. A table gives its columns in the order it declares them,
which is `column_id`'s, leaving out a `HIDDEN` period column and, when the table has a column set, its sparse columns, which the
set stands for; a relation gives the columns its symbol names; a temporary table gives its columns as the walk knows them at
that statement, through the names in force, as for any column of it. What it gives is a `StarExpansion`: an `ExpandedStar` with
the columns, an `UnlistedStar` naming the relation whose columns can't be listed (a caller's `#t`), whose reason the reader
finds in what that relation holds, or an `UnexpandedStar` with a binding problem (a table not in the project, a graph table,
whose star adds pseudo-columns the server names, or a qualifier naming a database).

When SQL Server binds a star depends on the module: a procedure and the functions that run statements bind it when they run, a
view and an inline function the star of their own select list when they are created (one in a subquery of either, when they
run). Every star is bound here against the tables as the project defines them,
assuming a deployment that refreshes the modules created earlier, as SqlPackage does
([decision 0047](../decisions/0047-a-star-is-bound-against-the-tables-the-project-defines.md)).

### The scopes of a FROM

The sources of one FROM clause are in one scope, with joins recording which side each is on (an outer join's optional side,
or an `OUTER APPLY`'s right side, `OptionalSide`). Two scopes are less obvious:

- **A derived table** (`FROM (SELECT …) AS d`) reads the enclosing query's scope, never its siblings in the same FROM. Its
  alias names it in that FROM only, so an inner query may use the same alias for something else.
- **`APPLY`'s right side** reads its left input's sources too, and only those: not a source listed before it with a comma
  (observed: `An_applied_query_sees_its_left_input_only`). The binder gives it a scope of those sources, the same objects, so
  what is known of them stays keyed to them.

### Variables and declarations

SQL Server compiles a body's declarations rather than running them: a variable declared in a branch that is never taken
still exists after it (observed: `A_variable_or_table_variable_declared_in_a_branch_not_taken_still_exists`). So binding reads
a body's declarations once, from the whole of it (`BodyDeclarations`): its parameters, its variables, its table variables (a
multi-statement function's return table among them) and its cursors. A variable reference resolves to a `ParameterVariable`,
a `LocalVariable` or an `UndeclaredVariable` (`VariableResolution`), and the inference reads a variable only through that: its
declared type from the declaration, and its value from the walk's state, which keeps it under the declared name, so two
spellings of one variable are one. A temporary table's state is kept under its name too, because the name is its identity
across a call: a procedure reads its caller's `#t` by name. A body's own tables are `DeclaredTable`s, named as the body
writes them, with their sigil, and belonging to no schema; the schema's rules read them as they read a schema table (`ITable`).

## The bound tree, and binding once

What the binder gives is a tree. A `QueryScope` is immutable: its parent, its sources and the names in force. A `BoundQuery`
is a query specification with its scope, or a set operation or parenthesis over bound queries, and a derived table's or
`APPLY`'s query is bound inside the scope of the query that reads it, so the whole statement's queries are one structure.

Binding is a function of three things: the fragment, the scope it is read inside, and the names in force
(`NameEnvironment`, compared by value). Nothing else changes what a name means, so each result is kept for those three
(`Bindings`), and a query is bound once for each set of names it is read with, the first time it is read. A loop's body is
read once a pass, a recursive CTE's members once a step, an `IF`'s two arms from two states; each statement is bound again
only if a temporary table's columns differ there. This is binding on demand, as a compiler's semantic model binds a node when
it is first asked about and keeps the answer, rather than one walk over every statement up front: Inference reads a body in
an order (the paths, the passes) that decides which names are in force where, and binding when asked follows that order
without a second walk that would have to know it ([decision 0033](../decisions/0033-binding-is-on-demand-and-kept-for-each-set-of-names.md)).

## How the inference reads it

Inference never looks a name up. Every expression is read in a `ReadScope`: the bound `QueryScope`, what each of its sources'
relations holds, and the state the statement starts from. When a query's scope is entered, Inference attaches a value to each
source that reads a relation, in the order the sources are written (so an `APPLY`'s right side sees its left input's): a
CTE's from its fixed point, a derived table's from its query, a temporary table's from the state, a view's from its summary.
A column reference is then `Binder.Resolve`'s answer and the value attached to that source, and nothing more.

An architecture test holds the line (`NameResolutionTests`): it reads Inference's compiled code and fails on any method that
reads what a resolver reads (a source's alias, a column reference's parts, a table reference's name, a schema object name's
parts, a declaration's name, a variable reference's name), apart from one listed with why (a scalar function's straight-line body substituted variable
by variable, which follows assignments rather than resolving names). A planted reader of each, in a method, a lambda, an
iterator and a local function, must be found.

## Why binding is a layer of its own

The obvious alternative is to bind as you infer: the binder asks the inference, for each derived table, view or call it
meets, to evaluate it and hand back its column names, and the inference keeps CTE scoping and temporary tables in its own
state, set before each statement. It works, but binding then can't run without the inference, so a tool that needs only to
know what names mean (a linter, lineage, a rename) would have to run the whole analysis, and each scoping rule is spread
across two layers. Apart, the binder is a pure function over syntax and the model, testable on its own (`BinderTests`), and
Inference is about values only.

The other alternative, one eager walk binding every statement before inference starts, loses on temporary tables: their
columns at a statement depend on the path, so an eager binder would need its own flow analysis, which is Inference's.

## What it deliberately doesn't do

- **No values.** What a column holds is Inference's; binding needs only names.
- **No guesses.** A name in another database, a synonym in FROM and a dynamic SQL string are unknowns with reasons, never
  bound to whatever they might mean.
- **No catalogue lookups at run time.** Everything comes from the project's scripts, as everywhere in haruspex.

## Reading further

- Andrew W. Appel, *Modern Compiler Implementation in ML* (Cambridge University Press, 1998), chapter 5, "Semantic
  analysis": symbol tables, and functional and imperative environments.
- Alfred V. Aho, Monica S. Lam, Ravi Sethi and Jeffrey D. Ullman, *Compilers: Principles, Techniques, and Tools*, second
  edition (Addison-Wesley, 2006), section 2.7, "Symbol tables", on scopes and the most closely nested rule.
- SQL Server's own documentation, for the rules this chapter models: deferred name resolution, in the `CREATE PROCEDURE`
  reference, and the `WITH common_table_expression`, `FROM` and subquery references.
