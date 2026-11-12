# The emitter plans, lowers and prints

**Date:** 12 Nov 2026

**Decision.** The Zod emitter works in three stages, each in its own folder of `Haruspex.Emit.Zod`: a *plan* of what the
module declares, in what order and under what names; a *lowering* of each IR type to a small model of Zod expressions,
records that compare by value; and a *printer* that writes the model as TypeScript and is the only code that knows its
syntax. Walks over the IR's types go through `IrTypeTree` (one type's structure) and `IrTypeGraph` (the document's
references, cycles, order and encodings), in the IR project, rather than each walking the tree itself.

**Context.** The emitter writes one idiom of one library, but it makes many decisions on the way: which check each fact
becomes, which facts Zod can't hold and must be noted, the order of union variants and declarations, getters for recursion,
what to import. Its output is a public claim, so each decision should be testable on its own, and the generated code is to
improve (merging identical shapes, dropping redundant checks), which needs schemas that can be compared and rewritten
before they are text.

**Alternatives.**
- *String templates*, text built while walking the IR. Short to write, but every decision is visible only in the final text,
  two schemas can be compared only as text (the trap of [decision 0022](0022-values-compare-by-structure.md)), and quoting and
  escaping happen wherever text is made.
- *A syntax tree of TypeScript*, printed by a library. Correct for any program, but large for the dozen forms written here;
  it makes the lowering speak TypeScript's grammar instead of Zod's, so "is this check redundant?" becomes a question about
  call expressions; and the TypeScript compiler, the obvious library, is a JavaScript program that a .NET emitter would have
  to run.
- *A template engine* (Scriban, Razor). The same as string templates with a second language for the logic, and a dependency.
- *A visitor on the IR's types* instead of `IrTypeTree`. Every node would gain a method that accepts a visitor and every
  walk a class, and each visitor would still recurse into children itself. The IR's types are data in a closed set, which C#'s pattern matching
  already handles; what was repeated was the knowledge of each node's children, and one function now holds it.

**Reason.** A model between deciding and writing lets each stage be tested alone and lets later stages reason about the code
in Zod's terms, at the cost of a few small records; and one place that knows a type's structure means a new kind of node
fails loudly in every walk rather than being skipped in some.

**See also:** [Emitting code](../internals/11-emitting-code.md).
