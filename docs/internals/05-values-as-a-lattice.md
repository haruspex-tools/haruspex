# Values as a lattice

The inference never computes with values. It computes with descriptions of them, *abstract values*: "an `int`, never null", "an
array of these objects, at least one", "unknown, because the shape depends on dynamic SQL". Each description says what the
value's shape is, why it may be null, and what is known beyond its type (its facets). This chapter is about what can be
done with such descriptions: telling whether two are the same, ordering them by how much they allow, and joining two into
one that allows both. [Summaries and recursion](07-summaries-and-recursion.md) introduced the order and the join in a few
lines; this chapter treats them properly.

Its first part, equality, is written. The order, the join, and why SQL Server's type rules for `CASE` and `UNION` are not
a join are planned as the rest of the chapter.

## Equality

### Why an analysis needs to tell two values apart

An abstract interpreter asks "is this the same as that?" more often than any other question, and it acts on the answer
in ways that matter for soundness:

- **A fixed point stops when a step changes nothing.** A loop's body is read again and again from the state at its head,
  each pass joined in, until a pass gives back the state it started from; recursive functions, recursive procedures and
  recursive CTEs are solved the same way (the least fixed point of [chapter 7](07-summaries-and-recursion.md)). "Gives
  back the same" is an equality.
- **A summary is reused when its inputs are the same.** A procedure is walked once per context it is called from, and a
  table-valued function read once per set of arguments; each walk is kept in a cache keyed by those inputs. Finding the
  key is an equality.
- **A merge keeps one of two things that are the same.** Two paths that return the same result at the same position
  return one result; a union does not list a variant twice.

An equality that is wrong either way does harm, but not the same harm:

| The equality says | when the values are | At a fixed point | In a cache |
|---|---|---|---|
| equal | different | stops early, with a state that is not a fixed point: a claim that does not hold for every execution | returns a summary computed for other inputs: again a claim that may not hold |
| different | the same | takes passes it didn't need, or reaches its limit and gives up with an unknown | walks again what it had walked |

The first row breaks the rule every claim rests on ([decision 0002](../decisions/0002-unknown-with-a-reason-never-a-guess.md));
the second costs time, or precision. So an equality for an analysis must never say "equal" of two values that something
later could tell apart ([decision 0022](../decisions/0022-values-compare-by-structure.md)).

These are the places in haruspex where an equality decides:

| Where | Compares | Code |
|---|---|---|
| A `WHILE` loop's fixed point | the state: temporary tables, variables, traced JSON, result sets | `FlowState` |
| A recursive CTE's fixed point | each column's value | `Value` |
| Recursive functions' fixed point | each function's summary | `FunctionReturn` |
| Recursive procedures' fixed point | the result sets returned | `ResultTrack` |
| The cache of procedure walks | the procedure and the tables it starts from | `CallContext` |
| The cache of table-valued function reads | the function and each argument's value | `ViewSummaries` |
| Joining two values | whether they are already the same | `ShapeJoin.Either`, `ShapeJoin.Content` |

### What an equality must satisfy

Three laws make a relation an *equivalence*, the kind of equality the rest of the analysis can lean on:

- **Reflexive**: every value equals itself.
- **Symmetric**: if a equals b, b equals a. A join must not depend on which path came first.
- **Transitive**: if a equals b and b equals c, a equals c. A cache is a hash table, and a hash table assumes it.

Two more follow from how the equality is used:

- **Equal values hash equal.** A dictionary looks a key up by its hash first; two equal keys with different hashes are
  two entries, and the cache misses.
- **Equal values can't be told apart by anything that follows.** If two descriptions are equal, the analysis must be free
  to keep either: every later step must give the same result from both. The literature calls an equivalence with this
  property a *congruence* for those steps. It is the law that decides which facts an equality must compare: all of them
  that some later step reads.

Where two representations mean the same thing, the last law suggests choosing one, a *canonical form*, so that equal
meanings are equal representations. Facets with nothing in them are stored as no facets (`IrScalar.Facets`), and an array
whose items are never null has an empty list of reasons, never a missing one (`ArrayShape`). Not every case is
canonical: a value set keeps its items in the order they were found, so two sets of the same items in another order are
different records. An equality stricter than meaning is safe, by the table above: it costs a pass or a cache entry.

`ValueEqualityTests` checks the first four laws over a sample of values and shapes generated from fixed seeds. For the
fifth it checks the condition it rests on, that a value differing in any one fact, however deeply nested, is not equal:
a congruence for every later step can only be argued step by step, and the steps are the next part of this chapter.

### Records compare by value, except their lists

C# gives a `record` an equality that compares every field. That is the right equality for descriptions, and it is why every
shape, value and IR node is a record. But a field's own equality is used, and .NET's lists (`IReadOnlyList<T>`, `List<T>`,
`T[]`, even `ImmutableArray<T>`) compare by reference. So two records that describe the same thing, built apart, are not
equal when either holds a list:

```csharp
var a = new IrField("total", type, IrPresence.Always, [new IrNullReason(IrNullRule.NullableColumn, "dbo.t.a")]);
var b = new IrField("total", type, IrPresence.Always, [new IrNullReason(IrNullRule.NullableColumn, "dbo.t.a")]);
// With IReadOnlyList<IrNullReason>, a.Equals(b) is false: the two lists are different objects.
```

Nearly every description holds a list: the reasons a value may be null, an object's fields, a union's variants, a value
set's items. Each of them has the type `EquatableArray`: an immutable sequence equal to another when its
items are equal in the same order, with a hash combined from its items. With it, a record's generated equality is the
structural equality the laws ask for, and `a.Equals(b)` above is true. It is a class rather than a struct, so a list that
may be missing (an object's results, when they are unknown) stays a nullable reference with no third state between empty
and missing.

The states that are classes rather than records (`FlowState`, `ResultTrack`) and the record that holds a dictionary
(`CallContext`) compare their parts the same way, their maps entry by entry (`ImmutableMaps`). Their keys are SQL names,
which SQL Server compares ignoring case, so `#Lines` and `#lines` are one key. A few collections still compare by
reference because nothing compares them: a document's map of named types, and the inference's private working records
([decision 0022](../decisions/0022-values-compare-by-structure.md) lists them).

### Why comparing rendered text was a trap

There is a tempting way round reference equality: write each value out as text, and compare the strings. The IR's writer
is already deterministic, so a type written twice gives the same text, and one function covers every kind of node.
The inference did this, and the trap is in the reason it seems to work: the text is written for a reader, and a reader doesn't
need everything that some later step of the analysis reads.

The text of a value left out the digits an integer constant counts (`Value.Digits`). SQL Server types an `int` literal
beside a decimal by its own digits, not the type's 10: `SELECT 1` beside a `decimal(38, 38)` gives `decimal(38, 37)`,
where an `int` column would give `decimal(38, 28)` (observed: `An_int_constant_counts_its_own_digits_beside_a_decimal`).
A CTE that doesn't recurse carries its literal through, and so does haruspex. A recursive CTE does not: its rows come from
the recursion, and the server counts the type's 10 digits however the members are written (observed:
`A_recursive_ctes_column_is_not_a_constant_even_when_every_member_is_one_literal`). The inference solves a recursive CTE as a
fixed point, and the join with a recursive member drops the anchor's digits. But a member that gives the anchor's literal
again gives a value whose *text* is the anchor's, so the fixed point compared by text stopped after one pass, keeping the
anchor's digits, and claimed `decimal(38, 37)`. Compared by structure, the digits differ, the iteration takes one more
pass, and the column is typed as the server types it:

```sql haruspex-example
CREATE TABLE shop.discount
(
    discount_id int             NOT NULL PRIMARY KEY,
    rate        decimal(38, 38) NOT NULL
);
GO
CREATE PROCEDURE shop.rates
AS
WITH flat AS (SELECT 1 AS rate UNION ALL SELECT 1 FROM flat WHERE 1 = 0)
SELECT d.rate FROM shop.discount AS d
UNION ALL
SELECT f.rate FROM flat AS f;
```

```ts haruspex-zod
export const RatesRowSchema = z.object({
  rate: z.number().multipleOf(1e-28) /* the mssql driver may round decimal(38,28) past its bounds, so they are not checked in a row */,
})
export type RatesRow = z.infer<typeof RatesRowSchema>

/** shop.rates: returns rows. */
export const RatesSchema = z.array(RatesRowSchema)
export type Rates = z.infer<typeof RatesSchema>
```

The digits were not the only fact the text left out. It also left out why a string's JSON is not known (`Value.JsonWhy`),
the sentence an unknown carries for its reader (`UnknownShape.Detail`), and how many rows a result set has
(`ResultShape.Cardinality`), which decided whether two results returned at one place were one. And the text could tell
apart what is the same: a function's name in two cases, which SQL Server treats as one name. None of these reached a
fixture's output, but each was an equality that said "equal" of values a later step could tell apart, or the reverse.

There are two smaller costs. One function served two purposes, so a change to how a value is shown to a reader (leaving out
a default, say) would have changed what the analysis treats as the same, silently. And a loop's state was written out in
full at every pass, to be compared once and thrown away.

### Alternatives

- **Text, more carefully written.** A text that included every fact would be a second, private serialisation of every
  description, kept in step with the records by hand: it fails as soon as a field is added and the writer is not.
  Structural equality includes a new field the moment it is declared.
- **Interning** (*hash-consing*): build each distinct value once, so that equal values are the same object and equality
  is a reference comparison. It makes equality as cheap as it can be, at the price of a table every constructor goes
  through, and of `with`, which builds a value outside it. Nothing has measured equality as a cost worth that.
- **A comparer per place** that compares only what that place cares about. Each place would hold its own idea of
  sameness, and the laws would have to be argued once per comparer. There is one equality, the records', and a place
  that deliberately ignores a fact says so in code: `ShapeJoin.Either` takes two values that differ only in their digits
  and their JSON's why as one value, because it combines the digits itself and keeps the first value's why.
- **`ImmutableArray<T>` for the lists.** It is immutable, but its equality compares the array it wraps, so it has the same
  trap as any other list.

### Closed vocabularies

A reason a value may be null, a reason a value is unknown, a scalar's format, an override's setting and where a fact came
from are each one of a fixed list. Each is an enum (`IrNullRule`, `IrUnknownReason`, `IrFormat`, `IrOverrideSetting`,
`IrFactSourceKind`), so a misspelt reason doesn't compile and two reasons are equal exactly when they are the same member.
The name each member has in the IR's JSON is part of the format, not of the analysis, and is written in one table per
vocabulary, `IrWireNames`, which the writer uses: `IrNullRule.NullableColumn` is `nullable-column` in the file, and only
there. `IrWireNamesTests` holds every name to the format, used by a fixture or not.

### What is not here yet

Equality is the first of the operations on values this chapter is to cover. The order between descriptions, the join,
their laws, and what stands in for a join where SQL Server's own rule is not one, are the chapter's planned remainder. Until
then, [Summaries and recursion](07-summaries-and-recursion.md), "Shapes, ordered", gives the ideas in brief.

## Reading further

- Anders Møller and Michael I. Schwartzbach, *Static Program Analysis*, chapters 4 and 5: lattices, and the fixed points
  whose stopping test this chapter is about.
- Jean-Christophe Filliâtre and Sylvain Conchon, "Type-safe modular hash-consing" (ML Workshop, 2006): interning, the
  alternative haruspex did not take, done well.
