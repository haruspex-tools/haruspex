# A switch over a closed hierarchy is exhaustive, and the build checks it

**Date:** 13 Nov 2026

**Decision.** A switch over a closed hierarchy (an abstract class of haruspex whose constructors no other assembly can call,
decision [0031](0031-the-model-is-closed-hierarchies-of-records.md)) handles every case, and the build fails when it doesn't,
as it does for a Swift enum. The project's own analyser, in `analysers/` (`ExhaustiveSwitchSuppressor`, `ExhaustiveSwitchAnalyser`),
runs on every project:

- A switch expression has no discard arm. The compiler can't know the hierarchy is closed, so it warns that the switch is not
  exhaustive (CS8509, or CS8846 when an arm has a `when` clause), which the build treats as an error; the suppressor removes
  that warning where the arms without a `when` clause cover every case, and only there.
- A switch statement without `default` must handle every case (HX0001); the compiler checks no switch statement.
- A switch that handles every case may not also have a discard arm, or a `default` that only throws (HX0002): such an arm can
  never run, and it would take a case added later without a word. A discard over a value that may be null is its null arm,
  and stays.

A case is matched by a type, a declaration, `or` of them, or a property pattern whose properties are all `var x` or `_`.
Every sum type of the code is closed so: the model's, `RelationSymbol`, `Resolution`, `SourceTarget`, `Callee`, `BoundQuery`,
`CallTarget` and `FactSource` in Semantics, `IrType` and `IrPathSegment` in the IR, `Shape` and `TempTable` in Inference, the
emitter's model, and the config reader's `JsonValue`.

**Context.** C# checks a switch over an enum for completeness, but not one over a class: any class can be derived from
somewhere, so the compiler asks for a discard arm, and every switch over a closed hierarchy had one that threw (`_ => throw new
ArgumentOutOfRangeException(…)`). A case added to a hierarchy then compiled everywhere and failed at run time, at whichever
switch forgot it, if a test reached it. Binding's `Resolution` is matched across Inference, so the gap would have grown.

**Alternatives.**
- *The ExhaustiveMatching package*, the one made for this (BSD-3-Clause). Its last release was in May 2020; it needs an
  attribute on each root listing the cases again, and a call to its own throw helper in a discard arm the code would keep;
  and it ships a runtime library, which every packable layer would then depend on. That a hierarchy is closed, haruspex's
  constructors already say, with nothing listed twice and nothing shipped.
- *A test per hierarchy, by reflection.* It can check that the cases are what a list says, but not what a switch handles, so
  the switch that forgets a case still compiles.
- *Visitor interfaces* (an `IResolutionVisitor<T>` with a method per case). The compiler then checks every visitor, but each
  match becomes a class and a dispatch, where C# says the same with a switch expression; and the IR's and the model's readers,
  who are not haruspex, would have to write them too.
- *Waiting for the language.* Closed hierarchies have been proposed for C#, but are not in C# 14; when they come, the analyser
  goes and the hierarchies stay as they are.

**Reason.** The rule costs one small analyser built against the compiler's API (`Microsoft.CodeAnalysis.CSharp`, MIT,
Microsoft's), run at build time only: nothing of it ships, so trimming and Native AOT are untouched, and the lock files pin it.
It catches what the discard arms hid: a switch that forgets a case is an error where it is written. Its limits are stated: a
hierarchy of haruspex only (a library's abstract type may have cases this compilation can't see, so it is never taken to be
closed), cases read from what the compiler can see (a case only a friend assembly can see would not be one), and a property
pattern counts only when it can't fail. A test compiles each rule's cases and its refusals (`ExhaustiveSwitchTests`).

**See also:** `analysers/README.md`; decision [0031](0031-the-model-is-closed-hierarchies-of-records.md).
