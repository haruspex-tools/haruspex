# Emitting code

The IR says what each procedure returns; an emitter says it again in a language an application can use. For Zod that means a
TypeScript module of schemas and types, written so that a developer reading it can't tell a program wrote it. This chapter is
about how a program writes code in another language: why haruspex's Zod emitter works in three stages (plan, lower and
print) rather than with string templates or a syntax tree of TypeScript, and why the walks over the IR's types are gathered
into one graph that every reader asks.

## Writing code is compiling

An emitter is the back end of a small compiler. Its input is already analysed (the IR), and its output is source text in
another language. Compilers have long split that work into steps that each change one thing: they *lower* a program from a
high-level representation to a lower one, choosing for each construct what it becomes in the target, and only at the end
*print* or encode the result. Each step has one kind of decision to make, and the representation between two steps is
something a test can look at.

The Zod emitter does the same, in three stages:

```mermaid
flowchart LR
    ir["IrDocument"] --> typeGraph["IrTypeGraph"]
    ir --> plan["plan: ZodPlanner"]
    typeGraph --> plan
    plan --> lower["lower: ZodLowering"]
    lower --> model["ZodModule of Zod expressions"]
    model --> print["print: ZodPrinter"]
    print --> text["haruspex.zod.ts"]
```

- **Plan** (`ZodPlanner`) decides what the module declares, in what order and under what names, and refuses a document it
  can't write before anything is built: a name TypeScript reserves, a schema imported from two modules, a type that one
  result reads as rows and another as JSON. Its output, `ZodPlan`, is a list of entries, each a named object type or a
  procedure.
- **Lower** (`ZodLowering`, with `TypeLowering` for one type and `Facets` for a scalar's facts) turns each entry into a
  declaration built from a small model of Zod expressions (`ZodExpression`): a call chain on `z`, an object schema, a union, a
  reference to a schema declared elsewhere, JSON text, and a note for the reader. This is where every claim the IR makes
  becomes a Zod check, and every claim Zod can't hold becomes a note.
- **Print** (`ZodPrinter`) writes the model as TypeScript. It is the only stage that knows TypeScript's syntax: how a string
  is quoted and escaped, which keys need quotes, how a comment is kept from ending early, where lines break. It decides
  nothing about what the module says.

## Why not string templates

The obvious way to write code is to build its text while walking the input: return a field's code and its comment as a pair
of strings, append `.nullable()` to the code, find a union's variants again by searching a list. It works for a while, and
it has the faults string templates always have.

**A decision made where text is written can't be looked at.** Whether a field is nullable, which check a bound becomes, which
variant of a union comes first: each is settled in the middle of concatenation, and the only evidence is the final text, which
a test must search with regular expressions. With a model between the stages, a test of the lowering compares records
(`TypeLoweringTests` checks an array's items, length and nullability as an expression, not as a string), and a test of the
printer gives it an expression and reads one line.

**Text can't be compared for meaning.** Two schemas are the same when they check the same things, not when their text happens
to match, and comparing descriptions by their rendered text is a trap the guide's chapter on values describes
([Values as a lattice](05-values-as-a-lattice.md)). The model's records compare by value, so two expressions that say the same
are equal, which is what merging identical shapes into one declaration needs.

**Syntax leaks everywhere.** When every step writes text, every step quotes and escapes, each a little differently. Here
escaping is one function in the printer, so a key, an enum value and an import's module are quoted by the same rules, and a
`*/` in a name from SQL can't end a comment early wherever the name appears.

## Why not a syntax tree of TypeScript

The other well-trodden road is to build the target language's syntax tree and let a library print it: Roslyn's syntax
factory for C#, the TypeScript compiler's factory functions for TypeScript. That gives correct syntax for any program, and
haruspex writes only one idiom of one library. A full tree would make the lowering say `CallExpression(PropertyAccess(
Identifier("z"), "string"), [])` where it means "a string", and the questions the emitter asks of its own output ("is this
check redundant?", "are these two objects the same shape?") would be asked of TypeScript's grammar rather than in Zod's
terms. The TypeScript compiler is also a JavaScript program, which an emitter written in .NET would have to run.

So the model is Zod's vocabulary, small and closed: a dozen kinds of expression, each a record. It is as close to the target
as it can be while still holding decisions rather than characters, and it stops where Zod stops: the printer knows that a
union is written `z.union([…])`, and the lowering only that it is a union.

## The plan: order, sharing and refusing early

A TypeScript module is read top to bottom, and a `const` can't be used before it is declared, so a schema must come after the
schemas it refers to. The plan puts each procedure in the order of its id, and before each the named types its results
reach that no earlier procedure reached, those in an order where every type follows the types it refers to. That is a
*topological order* of the graph of references, and a type two procedures share is declared once, before the first.

A recursive type has no such order: a category's subcategories are categories. Zod 4's answer is a getter,
`get children() { return z.array(CategorySchema) }`, which reads the schema only when it is used, by which time the
module has declared it, and through which TypeScript infers the type without casts. The lowering makes a getter of every
reference back into the type's own cycle, whichever type of the cycle the plan declares first.

The plan also refuses, before anything is built, what the module can't say. The IR's names are identifiers by the format's
own rule, but a name TypeScript reserves (`string`, `class`) can't name a binding, and an imported schema must come from one
module and not take a name the module declares. These are refused with `ZodEmitException`, which the CLI reports as a
mistake in the config file.

## One graph of the types

A type tree invites a walk in every program that reads it: one to collect the imports, one to find JSON text, one to order
the declarations, one to follow references, one to find cycles, one to rename references. Written separately, each is a
`switch` over the kinds of node with its own recursion, and a walk that ends in a default doing nothing skips silently any
kind of node added after it was written.

So the structure is known in two places, both in the IR project:

- **`IrTypeTree`** knows the structure of one type: the children of each node, with the path segment that leads to each,
  every node within a type, and a copy with its references renamed. A node it doesn't know is an error, never a skipped
  branch. A walk asks it for children rather than matching node kinds itself.
- **`IrTypeGraph`** knows the document: which named types each refers to, which are reachable from a type, which lie on a
  cycle, a topological order, and how each type's values arrive (as rows, or as JSON, which is what a type inside JSON text
  always is). It is computed once from an `IrDocument`.

The cycles are the graph's *strongly connected components*: sets of types each of which reaches every other. They are found
with Tarjan's algorithm, as the inference finds functions that call each other ([Summaries and recursion](07-summaries-and-recursion.md)).
The same depth-first search gives the order: a type finishes only after every type it reaches, so the order in which types
finish puts each after the types it refers to, except inside a cycle, where no order can. Starting the search from the results,
in the order the IR file lists the objects, makes the module's order follow the document rather than the alphabet.

A few walks match on node kinds themselves, each for a reason:

- **The IR's writer and reader** write and read each node's own properties, which only a match on its kind can do.
- **Building the IR** (`IrBuilder`) and **converting a declared result back into the inference's shapes** (`IrShapes`) turn
  one structure into another, a different result per kind.
- **Comparing a declaration with what was inferred** (`ResultsComparison`) walks two types side by side, a pair of nodes at
  a time, and **applying an override** (`OverrideApplier`) follows one path down a type, a segment at a time.
- **The lowering** gives each kind its own Zod expression, which is its job.
- **The corpus figures** decide whether a field is JSON text of unknown content by looking through arrays, records and JSON
  text only, as the figures' definition says.
- **The differential tests' validator and coverage check** walk types beside real values, on purpose apart from the code
  whose claims they test.

## Reading further

- Andrew W. Appel, *Modern Compiler Implementation in ML* (Cambridge, 1998): intermediate representations and lowering.
- Philip Wadler, "A prettier printer" (1998), and Derek Oppen, "Prettyprinting" (*ACM TOPLAS*, 1980): printing as a stage of
  its own, separated from what is printed.
- Robert Tarjan, "Depth-first search and linear graph algorithms" (*SIAM Journal on Computing*, 1972): strongly connected
  components in one depth-first search.
- Thomas H. Cormen and others, *Introduction to Algorithms*, the chapter on elementary graph algorithms: topological order from
  the finishing times of a depth-first search.
