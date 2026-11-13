# Analysers of the project's own code

Build-time checks of haruspex's code that the SDK's analysers don't make. `Directory.Build.props` runs them on every project
as an analyser, never as a reference, so no package depends on them and nothing of them ships.

## A switch over a closed hierarchy is exhaustive

A closed hierarchy is an abstract class of haruspex whose constructors no other assembly can call (`private protected`, as
[decision 0031](../docs/decisions/0031-the-model-is-closed-hierarchies-of-records.md) writes them), so its cases are the
types its own assembly derives from it. C# checks a switch over an enum for completeness but not one over such a class, so
these do ([decision 0032](../docs/decisions/0032-a-switch-over-a-closed-hierarchy-is-exhaustive.md) argues why, and why not a
package). A generic hierarchy is closed in the same way, and a switch over it with type arguments has as its cases the cases
with the same arguments, which are what its patterns name. That holds when each case passes the root its own type parameters,
each once, and has no others. A hierarchy with a case of another shape (a parameter the root doesn't fix, an argument built from
one, a case closed over one argument while the switch's arguments are still parameters) is not taken as closed for that switch:
the compiler's own check stands, and the switch needs its discard arm. A case closed over arguments other than a switch's
concrete ones is not a case of it:

| Rule | What it says |
|---|---|
| `ExhaustiveSwitchSuppressor` (HXS0001, HXS0002) | A switch expression over a closed hierarchy needs no discard arm when its arms cover every case: it suppresses the compiler's CS8509 (or CS8846) there, and only there, so a switch that forgets a case still fails the build. |
| HX0001 (`ExhaustiveSwitchAnalyser`) | A switch statement over a closed hierarchy without `default` handles every case. |
| HX0002 (`ExhaustiveSwitchAnalyser`) | A switch that handles every case has no discard arm, and no `default` that only throws: it could never run, and would take a case added later without a word. |
| HX0003 (`ExhaustiveSwitchAnalyser`) | A case of a closed hierarchy that other assemblies can name is one they can name too, or their switches would miss it unseen. |

`ExhaustiveSwitchTests` compiles each rule's cases and refusals. The analysers target .NET Standard 2.0, as the compiler
that loads them requires, and are written against `Microsoft.CodeAnalysis.CSharp`, no newer than the compiler of the SDK in
`global.json`.
