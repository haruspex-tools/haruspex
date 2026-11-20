using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Inference.Typing;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.Tests.Inference;

/// <summary>
/// The laws of each fact's lattice and of <see cref="Value"/>'s, checked over elements generated from fixed seeds
/// (<see cref="LatticeSample"/>): the join is idempotent and associative as records, commutative up to witnesses
/// (<c>Same</c>), an upper bound of both sides, and agrees with the order; bottom and top behave; joining what adds nothing
/// gives the left side back as a record, which is what lets a fixed point stop; and equal elements hash equal. The guide's
/// chapter <c>docs/internals/05-values-as-a-lattice.md</c>, "The laws, tested", says why each matters.
/// </summary>
/// <remarks>
/// A law is checked over every pair, and associativity over every triple of the first <see cref="Triples"/> elements. A
/// failure names its seed and the elements, and the same seed reproduces it.
/// </remarks>
public class LatticeLawTests
{
    private const int PerSeed = 24;
    private const int Triples = 12;
    private static readonly SourceLocation At = new("p.sql", 9, 1);

    public static TheoryData<int> Seeds { get; } = [.. Enumerable.Range(0, 25)];

    /// <summary>One lattice under test: its elements, join, equivalence and order.</summary>
    /// <param name="Grouped">
    /// What a regrouping of joins keeps: <see cref="Exactness.Record"/> when it gives the same record, and
    /// <see cref="Exactness.Witnesses"/> when it may name another unknown or list a union's variants in another order, which
    /// depend on what was met first.
    /// </param>
    /// <param name="Least">
    /// Whether the join is the least upper bound in <c>Below</c>, so that the order and the join agree both ways. Where it is
    /// not (facets, JSON and values: two objects merged key by key allow a little more than their union), joining adds nothing
    /// only to what is below, and not always the other way.
    /// </param>
    private sealed record Lattice<T>(string Name, Func<LatticeSample, T> Draw, Func<T, T, T> Join, Func<T, T, bool> Same, Func<T, T, bool> Below, Exactness Grouped = Exactness.Record, bool Least = false)
        where T : notnull;

    private enum Exactness
    {
        /// <summary>Exactly the same record.</summary>
        Record,

        /// <summary>The same up to witnesses (<c>Same</c>).</summary>
        Witnesses,
    }

    private static readonly Lattice<EquatableArray<IrNullReason>> Reasons =
        new("reasons", s => s.Reasons(), NullReasons.Join, NullReasons.Same, NullReasons.Below, Least: true);

    private static readonly Lattice<IntDigits> Digits =
        new("digits", s => s.Digits(), IntDigits.Join, (a, b) => a == b, IntDigits.Below, Least: true);

    private static readonly Lattice<IrCardinality> Rows =
        new("rows", s => s.Rows(), Cardinalities.Join, (a, b) => a == b, Cardinalities.Below, Least: true);

    private static readonly Lattice<Box<IrFacets>> NumberFacets =
        new("facets of a number", s => new(s.NumberFacets()), (a, b) => new(ScalarFacets.Join(a.Of, b.Of)), (a, b) => ScalarFacets.Same(a.Of, b.Of), (a, b) => ScalarFacets.Below(a.Of, b.Of));

    private static readonly Lattice<Box<IrFacets>> StringFacets =
        new("facets of a string", s => new(s.StringFacets()), (a, b) => new(ScalarFacets.Join(a.Of, b.Of)), (a, b) => ScalarFacets.Same(a.Of, b.Of), (a, b) => ScalarFacets.Below(a.Of, b.Of));

    private static readonly Lattice<Shape> Content =
        new("JSON content", s => s.Content(3), JsonShapes.Join, JsonShapes.Same, JsonShapes.Below, Exactness.Witnesses);

    private static readonly Lattice<JsonSlot> Slots =
        new("JSON slots", s => s.Slot(), JsonSlot.Join, JsonSlot.Same, JsonSlot.Below, Exactness.Witnesses);

    private static readonly Lattice<Value> Values =
        new("values", s => s.Value(), (a, b) => Value.Join(a, b, At, "@v"), Value.Same, Value.Below, Exactness.Witnesses);

    /// <summary>A nullable reference as a non-null element, so that "no facets", the top, is an element like any other.</summary>
    private sealed record Box<T>(T? Of) where T : class;

    // ---- the laws, one test per law over every lattice ---------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_join_is_idempotent_as_a_record(int seed) => Each(new Laws(seed).Idempotent);

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_join_is_commutative_up_to_witnesses(int seed) => Each(new Laws(seed).Commutative);

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_join_is_associative_as_a_record_or_up_to_witnesses(int seed) => Each(new Laws(seed).Associative);

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_join_is_an_upper_bound_of_both_sides(int seed) => Each(new Laws(seed).UpperBound);

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_order_agrees_with_the_join_and_its_equivalence_is_the_same_relation(int seed) => Each(new Laws(seed).OrderAgrees);

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_same_up_to_witnesses_is_an_equivalence_the_join_respects(int seed) => Each(new Laws(seed).Congruent);

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Joining_what_adds_nothing_gives_the_left_side_back_and_settles_in_one_join(int seed) => Each(new Laws(seed).LeftBiased);

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Equal_elements_hash_equal(int seed) => Each(new Laws(seed).HashEqual);

    [Fact]
    public void A_join_of_one_type_written_with_and_without_its_arguments_is_written_as_the_server_describes_it()
    {
        // decimal and decimal(18,0) are one type (ScalarShape.SameType), so the join is typed; its text is the server's where
        // the two were written differently, and theirs where they agree.
        var bare = new Value(ScalarShape.FromIr(new IrScalar(IrScalarKind.Number, IrFormat.Decimal, "decimal")), []);
        var full = new Value(ScalarShape.FromIr(new IrScalar(IrScalarKind.Number, IrFormat.Decimal, "decimal(18,0)")), []);
        Assert.Equal("decimal(18,0)", Assert.IsType<ScalarShape>(Value.Join(bare, full, At, "@v").Shape).Type.Sql);
        Assert.Equal("decimal(18,0)", Assert.IsType<ScalarShape>(Value.Join(full, bare, At, "@v").Shape).Type.Sql);
        Assert.Equal("decimal", Assert.IsType<ScalarShape>(Value.Join(bare, bare, At, "@v").Shape).Type.Sql);
    }

    [Fact]
    public void The_samples_have_comparable_elements_and_elements_the_same_up_to_witnesses()
    {
        // Without both, the order and the equivalence above would be tested on equal elements alone.
        var values = Seeds.SelectMany(s =>
        {
            var draw = new LatticeSample(s.Data);
            return LatticeSample.Many(PerSeed, draw.Value);
        }).ToList();
        var pairs = values.SelectMany((a, i) => values.Skip(i + 1).Select(b => (a, b))).ToList();

        Assert.Contains(pairs, p => p.a != p.b && Value.Same(p.a, p.b));
        Assert.Contains(pairs, p => !Value.Same(p.a, p.b) && Values.Below(p.a, p.b));
        Assert.Contains(pairs, p => !Values.Below(p.a, p.b) && !Values.Below(p.b, p.a));
    }

    // ---- bottom and top ----------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Bottom_is_the_identity_of_the_join_and_top_absorbs_it(int seed)
    {
        var draw = new LatticeSample(seed);
        for (var i = 0; i < PerSeed; i++)
        {
            var reasons = draw.Reasons();
            Assert.Equal(reasons, NullReasons.Join(NullReasons.None, reasons));
            Assert.Equal(reasons, NullReasons.Join(reasons, NullReasons.None));

            var rows = draw.Rows();
            Assert.Equal(rows, Cardinalities.Join(IrCardinality.One, rows));
            Assert.Equal(IrCardinality.Many, Cardinalities.Join(rows, IrCardinality.Many));

            Assert.Equal(IntDigits.Unknown, IntDigits.Join(draw.Digits(), IntDigits.Unknown));

            Assert.Null(ScalarFacets.Join(draw.NumberFacets(), null));

            var slot = draw.Slot();
            Assert.Equal(slot, JsonSlot.Join(JsonSlot.Empty, slot));
            Assert.Equal(slot, JsonSlot.Join(slot, JsonSlot.Empty));

            var content = draw.Content(3);
            var unknown = draw.Unknown();
            Assert.Equal(unknown, JsonShapes.Join(unknown, content));
            Assert.IsType<UnknownShape>(JsonShapes.Join(content, unknown));

            // A value of unknown shape is the top of the shape's lattice; its reasons still join.
            var value = draw.Value();
            var top = Value.Unknown(unknown);
            Assert.IsType<UnknownShape>(Value.Join(value, top, At, "@v").Shape);
            Assert.IsType<UnknownShape>(Value.Join(top, value, At, "@v").Shape);
        }
    }

    // ---- facts dropped by default ------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Seeds))]
    public void A_with_drops_the_digits_to_unknown_unless_it_sets_them_and_keeps_every_other_fact(int seed)
    {
        // Value's copy constructor, which every `with` runs, leaves the digits out: a rule that forgets them claims less.
        var draw = new LatticeSample(seed);
        foreach (var value in LatticeSample.Many(PerSeed, draw.Value))
        {
            var reasons = draw.Reasons();
            var changed = value with { NullableBecause = reasons };

            Assert.Equal(IntDigits.Unknown, changed.Digits);
            Assert.Equal(new Value(value.Shape, reasons, value.IsLiveJson, value.JsonWhy) { Digits = IntDigits.Unknown }, changed);
            Assert.Equal(value.Digits, (value with { Digits = value.Digits }).Digits);
            Assert.Equal(value.Digits, value.WithNullableBecause(reasons).Digits);
            Assert.Equal(IntDigits.Unknown, new Value(value.Shape, value.NullableBecause).Digits);
            Assert.Equal(IntDigits.OfType, Value.Computed(value.Shape, value.NullableBecause).Digits);
        }
    }

    // ---- joins the laws found wrong ---------------------------------------------------------------------------------

    [Fact]
    public void Objects_whose_keys_are_present_in_different_ways_stay_apart_so_the_order_of_joins_does_not_matter()
    {
        // A key present as null and one left out when null can't be one field, and a key never null joined with either could
        // go either way: merged with whichever came first, the union depended on the order of the joins. Objects merge only
        // when each key is present the same way, which is an equivalence, so the grouping no longer matters.
        static FieldShape Name(IrPresence presence, params IrNullReason[] because) => new("name", Number, presence, [.. because]);
        var omitted = new ObjectShape([Name(IrPresence.OmittedWhenNull, Null)]);
        var nullable = new ObjectShape([Name(IrPresence.Always, Null)]);
        var never = new ObjectShape([Name(IrPresence.Always)]);

        Assert.Equal(JsonShapes.Join(JsonShapes.Join(omitted, nullable), never), JsonShapes.Join(omitted, JsonShapes.Join(nullable, never)));
        Assert.Equal(new UnionShape([omitted, never]), JsonShapes.Join(omitted, never));
    }

    [Fact]
    public void A_record_in_a_union_merges_with_another_record_as_two_records_alone_do()
    {
        var numbers = new RecordShape(Number, []);
        var texts = new RecordShape(Text, []);
        var tuple = new TupleShape([]);

        Assert.Equal(new RecordShape(new UnionShape([Number, Text]), []), JsonShapes.Join(numbers, texts));
        Assert.Equal(JsonShapes.Join(tuple, JsonShapes.Join(numbers, texts)), JsonShapes.Join(JsonShapes.Join(tuple, numbers), texts));
    }

    [Fact]
    public void An_object_of_no_keys_joins_a_record_as_the_record_in_any_order()
    {
        // '{' + coalesce(string_agg(…), '') + '}' is {} over no rows, and {} is a record with no keys
        // (docs/decisions/0087-keys-built-from-data-make-an-object-whose-keys-are-data.md).
        var empty = new ObjectShape([]);
        var numbers = new RecordShape(Number, []);
        var named = new ObjectShape([new FieldShape("id", Number, IrPresence.Always, [])]);

        Assert.Equal(numbers, JsonShapes.Join(empty, numbers));
        Assert.Equal(numbers, JsonShapes.Join(numbers, empty));
        Assert.True(JsonShapes.Below(empty, numbers));
        Assert.False(JsonShapes.Below(named, numbers));
        Assert.True(JsonShapes.Same(JsonShapes.Join(JsonShapes.Join(empty, named), numbers), JsonShapes.Join(empty, JsonShapes.Join(named, numbers))));
    }

    [Fact]
    public void Equal_facts_from_different_sources_are_kept_with_the_left_ones_source()
    {
        // Both sides' values have exactly this length, or these digits, whichever constraint proved it: the fact holds of
        // either, and its source is a witness.
        IrFactSource type = new(IrFactSourceKind.Type, "char(10)"), check = new(IrFactSourceKind.Check, "ck_code");
        IrFacets Fixed(IrFactSource from) => new(Length: new IrLength(10, true, IrLengthUnit.Bytes, IrByteEncoding.SingleByte, from), Digits: new IrDigits(5, 2, from));

        Assert.Equal(Fixed(check), ScalarFacets.Join(Fixed(check), Fixed(type)));
        Assert.Equal(Fixed(type), ScalarFacets.Join(Fixed(type), Fixed(check)));

        IrFacets Least(IrFactSource from) => new(Min: new IrBound("0", true, from));
        Assert.Equal(Least(type), ScalarFacets.Join(Least(type), Least(check)));
    }

    [Fact]
    public void A_minimum_length_joins_to_the_shorter_and_a_maximum_to_the_longer()
    {
        // The looser of each: a value of either side may be as short as the shorter minimum and as long as the longer maximum.
        IrFactSource type = new(IrFactSourceKind.Type, "nvarchar(10)");
        IrFacets Lengths(int least, int most) => new(Length: new IrLength(most, false, IrLengthUnit.Utf16, IrByteEncoding.Unknown, type), MinLength: new IrMinLength(least, type));

        Assert.Equal(Lengths(1, 10), ScalarFacets.Join(Lengths(5, 10), Lengths(1, 6)));
        Assert.Equal(Lengths(1, 10), ScalarFacets.Join(Lengths(1, 6), Lengths(5, 10)));
        Assert.True(ScalarFacets.Below(Lengths(5, 6), Lengths(1, 10)));
        Assert.False(ScalarFacets.Below(Lengths(1, 10), Lengths(5, 6)));
    }

    [Fact]
    public void Each_facet_is_ordered_by_what_it_allows()
    {
        // One case per clause of ScalarFacets.Below: tighter is below, looser is not.
        IrFactSource from = new(IrFactSourceKind.Check, "ck_t_a");
        IrFacets Max(int value, bool isFixed) => new(Length: new IrLength(value, isFixed, IrLengthUnit.Utf16, IrByteEncoding.Unknown, from));
        IrFacets Least(string value, bool inclusive) => new(Min: new IrBound(value, inclusive, from));
        IrFacets Most(string value, bool inclusive) => new(Max: new IrBound(value, inclusive, from));
        IrFacets In(params string[] items) => new(Values: new IrValueSet([.. items.Select(i => new IrValue(i, false))], IrValueMatch.Exact, [from]));
        IrFacets Digits(int precision, int scale) => new(Digits: new IrDigits(precision, scale, from));

        Assert.True(ScalarFacets.Below(Max(5, isFixed: true), Max(5, isFixed: false)));
        Assert.True(ScalarFacets.Below(Max(5, isFixed: false), Max(10, isFixed: false)));
        Assert.False(ScalarFacets.Below(Max(10, isFixed: false), Max(5, isFixed: false)));
        Assert.False(ScalarFacets.Below(Max(5, isFixed: false), Max(5, isFixed: true)));
        Assert.False(ScalarFacets.Below(Max(5, isFixed: true), Max(10, isFixed: true)));

        Assert.True(ScalarFacets.Below(Least("1", inclusive: true), Least("0", inclusive: true)));
        Assert.False(ScalarFacets.Below(Least("0", inclusive: true), Least("1", inclusive: true)));
        Assert.True(ScalarFacets.Below(Least("0", inclusive: false), Least("0", inclusive: true)));
        Assert.False(ScalarFacets.Below(Least("0", inclusive: true), Least("0", inclusive: false)));
        Assert.True(ScalarFacets.Below(Most("0", inclusive: true), Most("1", inclusive: true)));
        Assert.False(ScalarFacets.Below(Most("1", inclusive: true), Most("0", inclusive: true)));
        Assert.True(ScalarFacets.Below(Most("1", inclusive: false), Most("1", inclusive: true)));
        Assert.False(ScalarFacets.Below(Most("1", inclusive: true), Most("1", inclusive: false)));

        Assert.True(ScalarFacets.Below(In("1"), In("1", "2")));
        Assert.False(ScalarFacets.Below(In("1", "2"), In("1")));
        Assert.False(ScalarFacets.Below(In("3"), In("1", "2")));

        Assert.True(ScalarFacets.Below(Digits(5, 2), Digits(5, 2)));
        Assert.False(ScalarFacets.Below(Digits(5, 2), Digits(6, 2)));

        Assert.True(ScalarFacets.Below(In("1"), null));
        Assert.False(ScalarFacets.Below(null, In("1")));
    }

    private static readonly ScalarShape Number = ScalarShape.FromIr(new IrScalar(IrScalarKind.Number, IrFormat.Int32, "int"));
    private static readonly ScalarShape Text = ScalarShape.FromIr(new IrScalar(IrScalarKind.String, null, "nvarchar(max)"));
    private static readonly IrNullReason Null = new(IrNullRule.NullableColumn, "dbo.t.a");

    // ---- the server's type rule is not a join -----------------------------------------------------------------------

    [Fact]
    public void The_servers_chain_rule_is_a_fold_from_the_left_and_not_associative()
    {
        // A_chain_is_typed_left_to_right_so_money_absorbs_an_integer_before_a_decimal: bigint, money, decimal(5,2) is
        // decimal(19,4), money having taken the bigint in; grouped the other way, decimal(5,2) and money first, it is
        // decimal(23,4). So the rule is folded from the left over the values as written, and never regrouped as a join may be.
        var (bigint, money, small) = (Operand.OfType(ScalarType.Parse("bigint")!), Operand.OfType(ScalarType.Parse("money")!), Operand.OfType(ScalarType.Parse("decimal(5,2)")!));

        Assert.Equal("decimal(19,4)", TypeChain.Of([bigint, money, small])!.Text);
        Assert.Equal(TypeChain.Of([bigint, money, small]), TypeChain.Of([Operand.OfType(TypeChain.Of([bigint, money])!), small]));
        Assert.Equal("decimal(23,4)", TypeChain.Of([bigint, Operand.OfType(TypeChain.Of([money, small])!)])!.Text);
        Assert.Equal("decimal(23,4)", TypeChain.Of([bigint, small, money])!.Text);
    }

    // ---- the laws ----------------------------------------------------------------------------------------------------

    private static void Each(Action<object> law)
    {
        law(Reasons);
        law(Digits);
        law(Rows);
        law(NumberFacets);
        law(StringFacets);
        law(Content);
        law(Slots);
        law(Values);
    }

    /// <summary>Each law, over one lattice's sample drawn from <c>seed</c>.</summary>
    private sealed class Laws(int seed)
    {
        public void Idempotent(object lattice) => Run(lattice, new Idempotence());

        public void Commutative(object lattice) => Run(lattice, new Commutativity());

        public void Associative(object lattice) => Run(lattice, new Associativity());

        public void UpperBound(object lattice) => Run(lattice, new Bound());

        public void OrderAgrees(object lattice) => Run(lattice, new Agreement());

        public void Congruent(object lattice) => Run(lattice, new Congruence());

        public void LeftBiased(object lattice) => Run(lattice, new LeftBias());

        public void HashEqual(object lattice) => Run(lattice, new Hashing());

        private void Run(object lattice, ILaw law)
        {
            switch (lattice)
            {
                case Lattice<EquatableArray<IrNullReason>> l:
                    law.Check(l, seed);
                    break;
                case Lattice<IntDigits> l:
                    law.Check(l, seed);
                    break;
                case Lattice<IrCardinality> l:
                    law.Check(l, seed);
                    break;
                case Lattice<Box<IrFacets>> l:
                    law.Check(l, seed);
                    break;
                case Lattice<Shape> l:
                    law.Check(l, seed);
                    break;
                case Lattice<JsonSlot> l:
                    law.Check(l, seed);
                    break;
                case Lattice<Value> l:
                    law.Check(l, seed);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(lattice));
            }
        }
    }

    private interface ILaw
    {
        void Check<T>(Lattice<T> l, int seed) where T : notnull;
    }

    private static List<T> Sample<T>(Lattice<T> l, int seed) where T : notnull
    {
        var draw = new LatticeSample(seed);
        return LatticeSample.Many(PerSeed, () => l.Draw(draw));
    }

    private static string Where<T>(Lattice<T> l, int seed, params T[] elements) where T : notnull =>
        $"{l.Name}, seed {seed}: {string.Join(" | ", elements.Select(e => Show(e)))}";

    /// <summary>An element written out in full, lists included, so that a failure shows what differs.</summary>
    private static string Show(object? element) => element switch
    {
        null => "none",
        Value v => $"{(v.IsLiveJson ? "live " : "")}{Show(v.Shape)} null[{Show(v.NullableBecause)}] digits {v.Digits.Least}-{v.Digits.Most}{(v.JsonWhy is null ? "" : " why " + v.JsonWhy.Detail)}",
        EquatableArray<IrNullReason> reasons => string.Join(",", reasons.Select(r => $"{r.Rule}@{r.At}")),
        ScalarShape s => $"{s.Type.Sql}{(s.Type.Facets is null ? "" : "{" + Show(new Box<IrFacets>(s.Type.Facets)) + "}")}",
        Box<IrFacets> { Of: var f } => f is null ? "no facets"
            : $"len {f.Length?.Value}{(f.Length?.IsFixed == true ? "!" : "")}/{f.Length?.From.Kind} min {f.MinLength?.Value} lo {f.Min?.Value}{(f.Min?.Inclusive == true ? "]" : ")")}/{f.Min?.From.Kind} hi {f.Max?.Value}/{f.Max?.From.Kind} in [{string.Join(",", f.Values?.Items.Select(i => i.Text) ?? [])}]",
        UnknownShape u => $"unknown {u.Reason} {u.Detail}@{u.At}",
        ArrayShape a => $"array({Show(a.Items)}; null[{Show(a.ItemsNullableBecause)}]; {a.MinItems}..{a.MaxItems})",
        TupleShape t => $"tuple({string.Join(", ", t.Items.Select(e => Show(e.Type) + " null[" + Show(e.NullableBecause) + "]"))})",
        ObjectShape o => $"object({string.Join(", ", o.Fields.Select(x => $"{x.Name}{(x.Presence == IrPresence.Always ? "" : "?")}: {Show(x.Type)} null[{Show(x.NullableBecause)}]"))})",
        RecordShape r => $"record({Show(r.Values)}; null[{Show(r.ValuesNullableBecause)}])",
        UnionShape u => $"union({string.Join(" | ", u.Variants.Select(Show))})",
        JsonTextShape j => $"text({Show(j.Content)})",
        JsonSlot j => j.NotJson is { } why ? $"not json {why.Detail}" : $"slot {Show(j.Content)}",
        _ => element.ToString() ?? "",
    };

    private sealed class Idempotence : ILaw
    {
        public void Check<T>(Lattice<T> l, int seed) where T : notnull
        {
            foreach (var a in Sample(l, seed))
                Assert.True(Equals(l.Join(a, a), a), Where(l, seed, a, l.Join(a, a)));
        }
    }

    private sealed class Commutativity : ILaw
    {
        public void Check<T>(Lattice<T> l, int seed) where T : notnull
        {
            var sample = Sample(l, seed);
            foreach (var a in sample)
            {
                foreach (var b in sample)
                    Assert.True(l.Same(l.Join(a, b), l.Join(b, a)), Where(l, seed, a, b, l.Join(a, b), l.Join(b, a)));
            }
        }
    }

    private sealed class Associativity : ILaw
    {
        public void Check<T>(Lattice<T> l, int seed) where T : notnull
        {
            var sample = Sample(l, seed).Take(Triples).ToList();
            foreach (var a in sample)
            {
                foreach (var b in sample)
                {
                    foreach (var c in sample)
                    {
                        var (left, right) = (l.Join(l.Join(a, b), c), l.Join(a, l.Join(b, c)));
                        Assert.True(l.Grouped == Exactness.Record ? Equals(left, right) : l.Same(left, right), Where(l, seed, a, b, c, left, right));
                    }
                }
            }
        }
    }

    private sealed class Bound : ILaw
    {
        public void Check<T>(Lattice<T> l, int seed) where T : notnull
        {
            var sample = Sample(l, seed);
            foreach (var a in sample)
            {
                foreach (var b in sample)
                    Assert.True(l.Below(a, l.Join(a, b)) && l.Below(b, l.Join(a, b)), Where(l, seed, a, b, l.Join(a, b)));
            }
        }
    }

    private sealed class Agreement : ILaw
    {
        public void Check<T>(Lattice<T> l, int seed) where T : notnull
        {
            // Joining a adds nothing to b only when a ⊑ b (both ways where the join is least); elements that say the same are
            // below each other (and only those, where it is least); and the order is transitive.
            var sample = Sample(l, seed);
            foreach (var a in sample)
            {
                foreach (var b in sample)
                {
                    var addsNothing = l.Same(l.Join(a, b), b);
                    Assert.True(l.Least ? addsNothing == l.Below(a, b) : !addsNothing || l.Below(a, b), Where(l, seed, a, b));
                    var both = l.Below(a, b) && l.Below(b, a);
                    Assert.True(l.Least ? both == l.Same(a, b) : !l.Same(a, b) || both, Where(l, seed, a, b));
                    foreach (var c in sample.Take(Triples))
                        Assert.True(!(l.Below(a, b) && l.Below(b, c)) || l.Below(a, c), Where(l, seed, a, b, c));
                }
            }
        }
    }

    private sealed class Congruence : ILaw
    {
        public void Check<T>(Lattice<T> l, int seed) where T : notnull
        {
            var sample = Sample(l, seed);
            foreach (var a in sample)
            {
                Assert.True(l.Same(a, a), Where(l, seed, a));
                foreach (var b in sample)
                {
                    Assert.True(l.Same(a, b) == l.Same(b, a), Where(l, seed, a, b));
                    if (!l.Same(a, b))
                        continue;
                    foreach (var c in sample)
                    {
                        Assert.True(!l.Same(b, c) || l.Same(a, c), Where(l, seed, a, b, c));
                        Assert.True(l.Same(l.Join(a, c), l.Join(b, c)), Where(l, seed, a, b, c));
                    }
                }
            }
        }
    }

    private sealed class LeftBias : ILaw
    {
        public void Check<T>(Lattice<T> l, int seed) where T : notnull
        {
            var sample = Sample(l, seed);
            foreach (var a in sample)
            {
                // Exactly the left side, except that a value of unknown shape takes in the reason naming the other side's
                // unknown, a witness, once: joining again then gives the same record, so an iteration settles a pass later.
                foreach (var b in sample.Where(b => l.Same(l.Join(a, b), a)))
                {
                    var joined = l.Join(a, b);
                    Assert.True(Equals(joined, a) || (a is Value { Shape: UnknownShape } && l.Same(joined, a) && Equals(l.Join(joined, b), joined)), Where(l, seed, a, b, joined));
                }
            }
        }
    }

    private sealed class Hashing : ILaw
    {
        public void Check<T>(Lattice<T> l, int seed) where T : notnull
        {
            List<T> all = [.. Sample(l, seed), .. Sample(l, seed)];
            foreach (var a in all)
            {
                foreach (var b in all.Where(b => Equals(a, b)))
                    Assert.Equal(a.GetHashCode(), b.GetHashCode());
            }
        }
    }
}
