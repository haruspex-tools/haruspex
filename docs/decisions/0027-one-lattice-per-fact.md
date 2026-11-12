# One lattice per fact, facts dropped to their top by default, and the type rule named apart from the join

**Date:** 12 Nov 2026

**Decision.** Each fact an abstract value holds has its own lattice, with one join, one structural equality and hash
(the record's), one equivalence up to witnesses (`Value.Same` and each fact's own) and, where it has one, an order: why
it may be null (`NullReasons`), the digits an `int` counts beside a decimal (`IntDigits`), a scalar's facets (`ScalarFacets`), JSON
built here (`JsonShapes`) and how many rows a result has (`Cardinalities`). `Value.Join` is their product, joined fact
by fact, and every place where two values meet calls it. The digits are dropped by default: a `with` copies every other
fact and leaves the digits unknown, the top of their lattice, unless it sets them, and a value built new has them unknown
unless its rule states them. The server's type rules read an operand's count from an `Operand`, built with the count
named, beside its `ScalarType`; a type itself carries none, so a rule's result can't keep one by accident. The server's
rule for the type of a `CASE`, `COALESCE` or set operation is `ScalarType.Chain`, a fold from the left, and is never used as
a join. Each fact's order is written apart from its join, so that the law that a join is an upper bound can fail.

Witnesses (the order of reasons, which unknown is named, a fact's source, the order of a value set's items and of a
union's variants, why a string's JSON is not known) are kept from the left side and not made canonical, because their
order reaches the IR and the Zod. So the joins are idempotent as records, commutative up to witnesses, and associative as
records for reasons, digits, rows and facets and up to witnesses for JSON and values. `LatticeLawTests` holds every law.

**Context.** Values were joined in several places, each its own way, and a fact that must not survive an operation was
cleared by hand (`with { Digits = null }`) where each rule remembered to. Forgetting was a false claim either way: a kept
literal's digits give bounds too narrow, and the type's digits a scale too coarse. The server's type rule and the join
of knowledge shared the word "join", though one is a fold that changes when regrouped and the other must not.

**Alternatives.**
- *One join that knows every fact.* A new fact has to be taught to it, and to every place that merged facts by hand; the
  laws are argued for the whole at once.
- *Clearing a fact by hand, or a required parameter.* A forgotten clear is silent, and a parameter doesn't reach `with`,
  which copies.
- *Dropping the digits to the type's own.* The commonest answer, but where the server folds a literal it is false; the
  top, any count from 1 to 10, makes a forgotten rule claim less instead.
- *Canonical witnesses* (sorted reasons, the least unknown, sorted variants), so that every law holds as records. They
  would change the order the IR and Zod are written in, which the reader sees and Zod's union matching depends on.
- *A property-testing library.* Seeded theories reproduce a failure from its seed; shrinking hasn't been needed yet.

**Reason.** A product of lattices needs no argument beyond its parts', so a new fact comes with its own small lattice and
laws, and the composed join stays right. Dropping a claim to its top makes the mistake that is easy to make, forgetting,
a loss of precision rather than a wrong claim, and the language enforces it rather than care.

**See also:** [the guide, "Values as a lattice"](../internals/05-values-as-a-lattice.md);
[decision 0022](0022-values-compare-by-structure.md), on equality.
