# Values compare by structure, never by their rendered text

**Date:** 12 Nov 2026

**Decision.** Two abstract values, shapes, IR types or states are the same exactly when their records are equal, field by
field, lists item by item (`EquatableArray`), and the maps of a state or a call context (`FlowState`, `CallContext`)
entry by entry. Every fixed point, cache and merge in the inference uses that equality; none compares values by writing
them out as text. Where two representations of "nothing known" mean the same, one is chosen: empty facets are stored as
none, and an array whose items are never null has an empty list of reasons.

**A whole document** compares as its file reads: its map of named types entry by entry, and its objects, unknowns and
overrides in the order the file sorts them, since their order in the records means nothing (`IrDocument`, added 12 Nov 2026
for the IR's round trip, in which a document read back from its file must equal the one written).

**Every record, in every layer** (amended 13 Nov 2026). A record's lists are `EquatableArray` and its maps `EquatableMap`
([decision 0034](0034-collections-that-compare-by-value-are-a-layer-of-their-own.md)), and `RecordEqualityTests` fails on a
field of any record that compares by reference. Three kinds of type are listed there, each with its reason: a type compared
by identity because identity is its meaning (a syntax node, a catalogue entry, the model, a bound scope); a type whose
equality is written by hand, with the test that holds its laws; and a working type that keys what it holds by identity
(`ReadScope`, the frames of `Facts`), which is a class, not a record, since nothing compares it. A value set's items and
sources keep the order they were found in, so two sets with the same items in another order are different records:
equality is stricter than meaning there, which costs a pass or a cache entry, never a wrong claim.

**Context.** Records' generated equality compares lists by reference, so the inference compared values by writing them as
text instead, through the IR's writer and a writer of its own. The text was written for a reader and left out facts a
later step reads: a constant's digits, why a string's JSON is not known, an unknown's sentence, a result's cardinality.
One of them was observable: a recursive CTE whose members repeat the anchor's literal stopped at the first pass with the
anchor's digits, and typed a column `decimal(38, 37)` where SQL Server gives `decimal(38, 28)`.

**Alternatives.**
- *Text that includes every fact.* A second serialisation of every description, kept in step with the records by hand;
  a field added to a record and not to the writer is a silent wrong equality.
- *Interning, so that equal values are one object.* Cheapest equality, but every constructor and every `with` must go
  through a table. Equality has not been measured as a cost worth that.
- *A comparer per place.* Each place's idea of sameness would need its own argument that it is an equivalence and safe for
  what follows; one equality is argued once.

**Reason.** An analysis may treat two values as one only when nothing that follows can tell them apart. The records hold
every fact by construction, so their equality includes a new fact the moment it is declared, and the laws an equality must
keep are tested once, over generated values (`ValueEqualityTests`).

**See also:** [the guide, "Values as a lattice"](../internals/05-values-as-a-lattice.md).
