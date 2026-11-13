# The typer apart from the walker

**Date:** 13 Nov 2026

**Decision.** Inference has two parts. The **typer** (`Typer`, with its collaborator `Cardinality`) is the expression and
query semantics: what an expression gives, and what a query returns, as a function of the syntax, its `ReadScope` and
readonly inputs fixed when it is built (the model, the binder, the `Summaries`, a function's argument values, `ANSI_NULLS`,
whether the body is read uncommitted). The **walker** (`Walker`) is the statement semantics: it owns the `FlowState` from
statement to statement, each statement's `Outcome`, the joins and the fixed points, and asks the typer for each expression
and query in the scope of the state before the statement. Writes, cursors and calls are statements, so they are parts of
the walker; what an `OUTPUT` knows of its write reaches the typer as an argument (`OutputReading`). Summaries of functions,
views and procedures are reached through one root (`Summaries`), built by `IrInference` and passed to each body read; no
summary holds another. An architecture test reads the compiled code for it (`TyperAndWalkerTests`): every field of the typer
is readonly, none holds a walker, none holds what can change (a list, a dictionary, an array, a type with a field that is
not readonly) unless it is listed with why it is no state of a read (the binder's and the summaries' memo tables, the
model), and nothing in the typer uses a member of the walker.

**Context.** One type did both: the walk, and every expression's value, with what an expression depended on besides its
syntax in fields set before a read and reset after (the facts proved, the query read, the aggregates around it, the
`json_query` argument, a `MERGE`'s actions, a write's target and unmatched source, a cursor's row). Each was right; together
they made a value depend on what was set when it was read, which a fixed point comparing states can't see. The guide's
chapter [Abstract interpretation](../internals/04-abstract-interpretation.md) gives the theory.

**Alternatives.**
- *Keep one type, with the fields documented.* The premise of the soundness argument (each transfer function a function of
  its inputs) would rest on every field being set and reset at the right moments on every path, which no test can check.
- *The typer split into a type per construct family*, each holding the others. A compositional semantics is one function
  defined by cases that call each other; types referring to each other would bring the shared state back. The typer is one
  type in several files, one family to each, and only `Cardinality`, which needs nothing of the typer but the scope, is a
  type of its own.
- *Writes as a collaborator of their own*, as `Cardinality` is. A write changes the state, fires triggers and may end the
  path, so its result is an `Outcome` and its input the walk's state: it is a transfer function of the statement semantics,
  and as a type apart it would need the walker's state passed in and handed back, which is the walker.
- *Summaries wired to each other after they are built* (a function summary told where the views are). It works, but each
  summary then depends on the order of construction and on a setter nothing else calls; one root passed as an argument has
  neither.

**Reason.** With the context an argument and the typer stateless, an expression's value is a function a reader can state,
reading an expression twice in one scope gives one value, and the walker's fixed points compare everything a pass depends
on. The architecture test keeps it so: a field set around a read, or a read of the walk's state, fails the build.

**See also:** [the guide, "Abstract interpretation"](../internals/04-abstract-interpretation.md); decisions
[0028](0028-one-fixed-point-and-one-environment-of-facts.md) and [0033](0033-binding-is-on-demand-and-kept-for-each-set-of-names.md).
