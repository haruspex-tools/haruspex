# Values as a lattice

The inference never computes with values. It computes with descriptions of them, *abstract values*: "an `int`, never null", "an
array of these objects, at least one", "unknown, because the shape depends on dynamic SQL". Each description says what the
value's shape is, why it may be null, and what is known beyond its type (its facets). This chapter is about what can be
done with such descriptions: telling whether two are the same, ordering them by how much they allow, and joining two into
one that allows both. [Summaries and recursion](07-summaries-and-recursion.md) introduced the order and the join in a few
lines; this chapter treats them properly.

It has three parts: equality; the order and the join, built one lattice per fact; and why SQL Server's type rule for
`CASE` and `UNION` is not a join. The statement walk that uses them is the subject of the next chapter,
[Flow, joins and fixed points](06-flow-joins-and-fixed-points.md).

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
| Joining two values | whether they are already the same | `JsonShapes.Join` |

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

A map a record holds is an `EquatableMap`, equal to another with the same keys holding equal values, in whatever order they
were added: the state (`FlowState`) and a call's context (`CallContext`) hold their tables, variables and traced JSON so.
The keys are names that compare as SQL Server compares them, ignoring case (`LocalName`, `ObjectName`), so `#Lines` and
`#lines` are one key, and the comparison is the key's own rather than a comparer each map must be given.

Every record of every layer is held to this by a test, `RecordEqualityTests`, which reads each record's fields and fails on
one that compares by reference, looking through a tuple or a nullable value at what it holds, and refusing `object` or a
type from outside haruspex it can't read unless it is listed. Where equality is not generated field by field, the test
lists the type with why: a syntax node or a catalogue entry, compared by identity because identity is what it means; an
equality written by hand, with the
test that holds its laws; and a working type that keys what it holds by identity (a scope keys its sources so, since two
sources of one table in two scopes are equal as records), which is a class, not a record, because nothing compares it
([decision 0022](../decisions/0022-values-compare-by-structure.md)).

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
  /** The driver may round decimal(38,28) past its bounds in a row, so they are not checked. */
  rate: z.number().multipleOf(1e-28),
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
  sameness, and the laws would have to be argued once per comparer. There is one equality, the records'. Where a question
  is about meaning rather than records ("do these two say the same?"), it is a second, named relation, the equivalence
  up to witnesses of "Witnesses" below, defined once per fact beside its join.
- **`ImmutableArray<T>` for the lists.** It is immutable, but its equality compares the array it wraps, so it has the same
  trap as any other list.

### Closed vocabularies

A reason a value may be null, a reason a value is unknown, a scalar's format, an override's setting and where a fact came
from are each one of a fixed list. Each is an enum (`IrNullRule`, `IrUnknownReason`, `IrFormat`, `IrOverrideSetting`,
`IrFactSourceKind`), so a misspelt reason doesn't compile and two reasons are equal exactly when they are the same member.
The name each member has in the IR's JSON is part of the format, not of the analysis, and is written in one table per
vocabulary, `IrWireNames`, which the writer uses: `IrNullRule.NullableColumn` is `nullable-column` in the file, and only
there. `IrWireNamesTests` holds every name to the format, used by a fixture or not.

## The order and the join

### Descriptions are ordered by what they allow

Each abstract value describes a set of real values: every value an expression could have on some execution. One
description is **below** another, written a ⊑ b, when it allows no value the other doesn't. "An `int`, never null" is below
"an `int`, may be null", which is below "unknown". Lower is more precise, and every description is sound as long as it
still allows every real value.

The **join** of two descriptions, a ⊔ b, is a description that allows everything either allows: what is known of a value
that may come from either side. It is what holds where two paths of a body meet, where a column of a temporary table is
written by two statements, and where a recursive CTE's members add rows to its anchor's. An ordered set in which any two
elements have a join is a **join-semilattice**, and that is all the analysis needs; a **lattice** also has a meet,
which nothing here uses. At the top is the description that allows anything, unknown, which claims nothing and so is
always sound.

The literature asks the join to be the *least* upper bound, the most precise description that allows both. The joins
here are upper bounds, and least where that is cheap, but not always: two JSON objects of the same keys are merged key
by key, which allows a little more than the union of the two would. An upper bound that is not least is sound, and it is
the price of shapes that stay small. What the analysis does rely on are the laws below.

### One lattice per fact

A description is several facts, each with its own order: the shape, why the value may be null, the digits an `int`
constant counts, a scalar's facets, whether it is live JSON. Each fact is a small lattice of its own, with its own join and
its own order, and a value's lattice is their **product**: one value is below another when each of its facts is, and the
join of two values joins them fact by fact.

| Fact | Its elements | Bottom | Top | Join | Code |
|---|---|---|---|---|---|
| Why it may be null | sets of reasons, each a rule at a place | no reason (never null) | none needed | union | `NullReasons` |
| Digits an `int` counts beside a decimal | exactly n, or from n to 10 | none: each exact count is minimal | 1 to 10 | the same count, or the range from the fewer to 10 | `IntDigits` |
| A scalar's facets | lengths, bounds, digits, value sets | none | no facets | facet by facet, the looser; a facet one side lacks is dropped | `ScalarFacets` |
| JSON built here | shapes, unions of shapes | none | unknown | key by key, item by item, or a union | `JsonShapes` |
| How many rows | one, at most one, any | one | any | the higher in that chain | `Cardinalities` |
| The value | all of the above, with whether it is live JSON | | | fact by fact | `Value.Join` |

Building the value's join from its facts' joins is what keeps it right as facts are added. A new fact comes with its own
small lattice and laws, tested on their own, and the product needs no new argument: a product of semilattices is a
semilattice. The alternative, one join that knows every fact, is how a forgotten fact gets merged by hand in one place and
not in another. The order of `IrCardinality` is written in `Cardinalities` by rank for the same reason: an order read from
an enum's values holds only as long as nobody reorders its members.

Two facts can't be joined apart, and are one fact here: a shape and whether it is live JSON. The same object shape means a
JSON object when it is live and a string holding one when it is not, so `Value.Join` joins the pair. A shape that can't be
joined as a SQL value (a string and a number, two different objects) is unknown there, since a column can't be a union of
types. JSON built here can always be joined exactly, as a union, so it has a lattice of its own, `JsonShapes`.

### The laws a join keeps, and why each matters

| Law | Says | Without it |
|---|---|---|
| Idempotent | a ⊔ a = a | a loop that meets the same state again would see it change, and never stop |
| Commutative | a ⊔ b = b ⊔ a | the result would depend on which path of an `IF` is written first |
| Associative | (a ⊔ b) ⊔ c = a ⊔ (b ⊔ c) | the result would depend on how many paths met at once, and in what grouping |
| An upper bound | a ⊑ a ⊔ b and b ⊑ a ⊔ b | a join could claim something one side doesn't guarantee: unsound |
| The order agrees | a ⊔ b = b only when a ⊑ b; both ways where the join is least | "nothing new" (the test a fixed point stops on) would mean something other than "already allowed" |

The upper-bound law is only as good as the order it is checked in. An order defined as "joining adds nothing" would make it
true of any join, however much the join claims. So each fact's order is written apart from its join, from what the fact
means: `NullReasons.Below` (a subset), `IntDigits.Below` (a count within the other's range), `Cardinalities.Below`,
`ScalarFacets.Below` (facet by facet, each as tight or tighter: a maximum length no longer, a minimum length no shorter, a
lower bound no lower, a value set a subset), `JsonShapes.Below` (by structure: anything below an unknown, a union below
what covers each of its variants) and `Value.Below`, the product of the facts'. A join that keeps the tighter of two bounds
then fails the upper-bound law. Where the join is not least, in facets, JSON and values, the laws ask only that joining adds
nothing to what is above.

`LatticeLawTests` checks each of them for every fact and for values, over elements drawn from small alphabets with fixed
seeds (`LatticeSample`), so that equal and comparable elements built apart turn up by chance. It also checks that bottom is
the join's identity and top absorbs it, and that equal elements hash equal. No property-testing library is used: seeded
theories reproduce a failure from its seed, which is what a law test needs, and shrinking a failure to its smallest form
has not been missed yet.

### Witnesses

Some of what a description holds is not knowledge but a **witness** to it: something that says *why* a fact holds, or
*which* of several equally good explanations is reported. The join keeps the first one met.

- **The order of reasons.** A list of reasons is a set, written in the order its reasons were found.
- **Which unknown.** Every unknown is the top, but each names its reason and place, and a join of two keeps the left one.
  The reason naming that unknown in the value's reasons goes with it.
- **A fact's source.** Of two equal bounds, lengths or digits from different constraints, the join keeps the left one's
  source. Either justifies the fact for both sides, since both sides' values meet the same bound.
- **The order of a value set's items and of a union's variants**, the left side's first.
- **Why a string's JSON is not known** (`Value.JsonWhy`), which only names the reason a later `json_query` reports.

Witnesses make the join commutative only *up to* them: `a ⊔ b` and `b ⊔ a` list the same reasons in another order, or
name another unknown. So each fact defines, beside its join, when two of its elements **say the same** (`NullReasons.Same`,
`ScalarFacets.Same`, `JsonShapes.Same`, `JsonSlot.Same`, `Value.Same`): equal but for witnesses. Everything else, the
digits, a facet's value, a key's presence, whether a value is live JSON, is compared exactly. Formally, the order is a
**preorder**, and the lattice is on its equivalence classes; "says the same" is that equivalence.

Which unknown is named, and in what order a union lists its variants, also depend on what was met first when three
sides are joined in two groupings, so associativity holds as records for reasons, digits, rows and facets, and up to
witnesses for JSON and values. Idempotence holds as records everywhere.

The witnesses could be made canonical (reasons sorted, the least unknown kept), and every law would then hold as
records. That was not done, because the order reaches the output. The IR lists reasons as they were found, and a union's
variants in their order; Zod tries a union's variants in order, and with objects that strip unknown keys the first variant
that matches decides which keys survive. What matters there is that the order is deterministic, which it is, since a body
is always walked the same way.

Witnesses also explain why a fixed point still stops. Its stopping test is structural equality, which compares witnesses
too, so it is finer than "says the same": it may take a pass more than it needs, never a pass fewer (the table under
"Equality"). And because each join keeps its left side's witnesses, a state joined with something that adds nothing comes
back as the same record, as long as the state is kept on the left. `LatticeLawTests` checks that too, and the one exception:
a value of unknown shape takes in the reason naming the other side's unknown once, and settles a pass later.

### Facts dropped by default

Some facts are claims that an operation can make false. The digits an `int` counts beside a decimal are one: SQL Server
counts a literal's own digits, so `1` beside a `decimal(38, 38)` gives `decimal(38, 37)`, but it counts the type's 10 for
anything it doesn't fold to a constant, so `1 + 1` gives `decimal(38, 28)` (observed:
`An_int_constant_counts_its_own_digits_beside_a_decimal`). Each rule that makes a new value from an old one must say
whether the digits survive. If a rule forgets, either answer can be false: keeping a literal's 1 where the server counts 10
claims bounds of ±9.99…, too narrow; dropping to 10 where it counts 1 claims a scale of 28, too coarse.

The lattice gives the safe default. A fact that no rule has stated is not "absent", it is unknown, the fact's top: for the
digits, any count from 1 to 10 (`IntDigits.Unknown`). Where that range changes the type, the type is unknown, with a
reason that names the expression; where it doesn't, nothing is lost. So a forgotten rule claims less, never
something false.

The default is enforced by the language rather than by care. A C# `with` copies a record through its copy constructor, and
`Value` declares its own, which copies every fact but the digits. So `value with { NullableBecause = … }` has unknown
digits unless the same `with` sets them, and a `Value` built new has unknown digits unless its rule states them:
`Value.Computed` for a column, a variable or a computed result, which the server never folds (`IntDigits.OfType`), a
literal's own count where one is read (`IntDigits.Literal`), and `Value.WithNullableBecause` where only whether it may be null
changes and every other fact passes on.

The server's type rules read the same count, one end of that range at a time. There it is not on the type at all: an
`Operand` carries it beside its `ScalarType`, every operand is built with its count named (`Operand.OfType`, or a number
for a literal), and a rule's result is a plain `ScalarType`, which has nowhere to keep one. So the count can't survive into
a result by accident, nor be dropped from an operand without the rule writing so.

The other facts survive a `with`. The shape, the reasons and whether a value is live JSON are what a `with` is written to
change, and the JSON's why is a witness. A fact earns the default when a rule that forgets it can make a claim false.

Alternatives:

- **Clear the fact by hand where it must not survive**, as `with { Digits = null }` at each such rule. Forgetting one is
  silent, and every rule that makes one value from another has to remember.
- **A required parameter**, so that every construction states the fact. It catches a value built new, but not a `with`,
  which copies.
- **A default of "its type's digits"** rather than unknown. It is the commonest answer, but a forgotten rule would then
  claim it where the server counts a literal's, which is false.

## A knowledge join is not a type rule

SQL Server also combines values. `CASE`, `COALESCE`, `IIF`, `CHOOSE` and the set operations give one type to values of
several, by **data type precedence**. It is tempting to call that a join, and it is not one.

The server's rule is a **fold from the left** (`TypeChain.Of`, observed:
`A_chain_is_typed_left_to_right_so_money_absorbs_an_integer_before_a_decimal`): it combines the first two types, then
that with the third, and so on. Combining is not associative: `money` takes a `bigint` in and stays `money`, a decimal then
takes the `money`'s 4 places; a decimal first takes the `bigint`'s 19 integer digits, and `money` adds its 4 places to
those. So the same three values give two types in two orders:

```sql haruspex-example
CREATE TABLE shop.price
(
    price_id int           NOT NULL PRIMARY KEY,
    units    bigint        NOT NULL,
    fee      money         NOT NULL,
    amount   decimal(5, 2) NOT NULL
);
GO
CREATE PROCEDURE shop.charges
AS
SELECT COALESCE(p.units, p.fee, p.amount) AS fee_first,
       COALESCE(p.units, p.amount, p.fee) AS amount_first
FROM shop.price AS p;
```

```ts haruspex-zod
export const ChargesRowSchema = z.object({
  /** The driver may round decimal(19,4) past its bounds in a row, so they are not checked. */
  fee_first: z.number().multipleOf(1e-4).nullable(),
  /** The driver may round decimal(23,4) past its bounds in a row, so they are not checked. */
  amount_first: z.number().multipleOf(1e-4),
})
export type ChargesRow = z.infer<typeof ChargesRowSchema>

/** shop.charges: returns rows. */
export const ChargesSchema = z.array(ChargesRowSchema)
export type Charges = z.infer<typeof ChargesSchema>
```

`fee_first` is `decimal(19,4)`, which a `bigint` may not fit, so converting `units` to it may overflow, NULL under a
caller's options, and the column may be null; `amount_first` is `decimal(23,4)`, which holds every value of all three.

The two operations answer different questions. The type rule says what SQL Server *makes* of the values: a type, and the
conversion of each value to it. The knowledge join says what haruspex *knows* of a value that comes from one side or the
other. A `CASE` uses each where it applies: the type rule for its type, the server's folding rule for
its digits (`IntDigits.Foldable`, which is not the join either: one `1` folds to 1 or stays 10), and the knowledge join
for why it may be null. Its facets are its type's, with the set of its literals where every result is one. So they are named apart, `TypeChain.Of` and `Typer.Chained`
for the rule and `Value.Join` and the facts' joins for knowledge, and nothing that wants one calls the other. Using the
type rule as a join would make a value's type depend on the order of paths, and regroup the server's fold, which the
server does not. `LatticeLawTests` holds the rule to its fold: grouping its first two types first gives its result, and
grouping its last two first does not.

## Reading further

- Anders Møller and Michael I. Schwartzbach, *Static Program Analysis*, chapters 4 and 5: lattices, product lattices,
  and the fixed points whose stopping test this chapter is about.
- Flemming Nielson, Hanne Riis Nielson and Chris Hankin, *Principles of Program Analysis*, chapter 4: abstract
  interpretation, and building a domain from smaller ones.
- Jean-Christophe Filliâtre and Sylvain Conchon, "Type-safe modular hash-consing" (ML Workshop, 2006): interning, the
  alternative haruspex did not take, done well.
