# Steps return outcomes; exceptions are for faults

**Date:** 13 Nov 2026

**Decision.** A step that can fail because of its input (reading the config file, selecting procedures, applying overrides,
emitting) returns an `Outcome<T>`: `Succeeded<T>`, its value with the warnings and notes it found, or `Failed<T>`, what it
found, at least one of it an error, and no value. Each case holds its invariant from its constructor. A step that can't fail
says so by returning `Succeeded<T>` (inference: a document and its warnings). Steps are composed with `Outcome<T>.Then`, which
runs the next step only on a value, and with `Outcome.Combine` and `Outcome.All` for steps that don't need each other's values,
which report what every one of them found. An exception is thrown only for a fault: a bug in haruspex, a broken argument from
its own code, or a failure of the machine that no step can describe better than the exception does. The CLI catches every
exception at its edge and exits with 3.

**Context.** A user's mistake travelled in several ways: a record of "a value or null, and problems" for each step
(`ConfigReadResult`, `ConfigApplied`, `SelectionResult`), problems added to a list passed in (`ICollection<Diagnostic>`), and
exceptions thrown for a mistake and caught by the caller (`PathException` inside the override walk, `ZodEmitException` caught
by the CLI as a usage error, `ConfigJsonException` inside the config reader). An exception for a mistake hides the path
from the step's signature, stops at the first problem, and makes a reader of the code look for the `catch` to know what
happens.

**Alternatives.**
- *Exceptions for mistakes*, the .NET default. A signature doesn't show them, a caller that forgets the `catch` turns a
  user's typo into a crash, and the first one thrown hides the rest, where a compiler reports every error it can.
- *A record with a nullable value and a list*, as before. "A value or null" is one of two cases written as nullable fields:
  nothing stops a value beside an error, or no value and no error, and each caller tests for null and trusts the list.
- *A library* (FluentResults, OneOf, LanguageExt). A dependency for two small records and three combinators, with names that
  aren't the domain's ("result" here means a result set), and none checks a switch over its cases as the build's
  analyser checks a closed hierarchy of ours ([decision 0032](0032-a-switch-over-a-closed-hierarchy-is-exhaustive.md)).
- *Problems added to a list passed in*, as inference's warnings were. The list is a second, hidden result, and a caller
  that passes none loses the warnings without a word.

**Reason.** What can go wrong is in the step's type, a failure can't carry a value, and every problem found travels with the
value to the edge, where the CLI writes them all. The ideas are railway-oriented programming and, for steps side by side,
applicative validation, which the guide's chapter on the command line explains.

**See also:** [decision 0036](0036-diagnostics-have-stable-codes.md); `src/Haruspex.Diagnostics/README.md`.
