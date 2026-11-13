using System.Collections.Immutable;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Model;

namespace Haruspex.Tests.Inference;

/// <summary>
/// The laws an equality must keep, checked for the inference's values and shapes over a sample generated from fixed seeds:
/// reflexive, symmetric and transitive; equal values hash equal; a value built again is equal to the first; and a value
/// differing in any one fact, however deep, is not equal. The fixed points and caches of the inference rest on these laws
/// (the guide's chapter <c>docs/internals/05-values-as-a-lattice.md</c>, "Equality").
/// </summary>
/// <remarks>
/// The sample is drawn from small alphabets (two locations, three null reasons, a few scalars), so that values equal without
/// being the same object turn up by chance and transitivity is tested on more than copies. The same seed always gives the
/// same sample, so a failure names its seed and is reproduced by it.
/// </remarks>
public class ValueEqualityTests
{
    private const int PerSeed = 40;

    public static TheoryData<int> Seeds { get; } = [.. Enumerable.Range(0, 25)];

    private static List<Value> Sample(int seed)
    {
        var generator = new Generator(new Random(seed));
        return [.. Enumerable.Range(0, PerSeed).Select(_ => generator.Value(depth: 3))];
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Equality_is_reflexive_and_a_value_built_again_is_equal_and_hashes_equal(int seed)
    {
        var (first, again) = (Sample(seed), Sample(seed));
        for (var i = 0; i < PerSeed; i++)
        {
            Assert.True(first[i].Equals(first[i]), $"seed {seed}, value {i}: not equal to itself");
            Assert.NotSame(first[i], again[i]);
            Assert.True(first[i] == again[i], $"seed {seed}, value {i}: not equal when built again: {first[i]}");
            Assert.Equal(first[i].GetHashCode(), again[i].GetHashCode());
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Equality_is_symmetric_and_transitive_and_equal_values_hash_equal(int seed)
    {
        // The sample and a copy built again, so that every value has at least one equal that is another object.
        List<Value> values = [.. Sample(seed), .. Sample(seed)];
        var equal = values.Select(_ => new bool[values.Count]).ToArray();
        for (var i = 0; i < values.Count; i++)
        {
            for (var j = 0; j < values.Count; j++)
            {
                equal[i][j] = values[i].Equals(values[j]);
                Assert.True(equal[i][j] == values[j].Equals(values[i]), $"seed {seed}: {i} and {j} are not symmetric");
                if (equal[i][j])
                    Assert.True(values[i].GetHashCode() == values[j].GetHashCode(), $"seed {seed}: {i} and {j} are equal but hash apart");
            }
        }

        for (var i = 0; i < values.Count; i++)
        {
            for (var j = 0; j < values.Count; j++)
            {
                if (!equal[i][j])
                    continue;
                for (var k = 0; k < values.Count; k++)
                    Assert.True(!equal[j][k] || equal[i][k], $"seed {seed}: {i} = {j} and {j} = {k}, but not {i} = {k}");
            }
        }
    }

    [Fact]
    public void The_sample_has_equal_values_that_are_not_copies_and_unequal_ones()
    {
        // Without both, transitivity above would hold of copies alone.
        var sample = Seeds.SelectMany(s => Sample(s.Data)).ToList();
        var pairs = sample.SelectMany((a, i) => sample.Skip(i + 1).Select(b => a == b)).ToList();

        Assert.Contains(true, pairs);
        Assert.Contains(false, pairs);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void A_value_that_differs_in_any_one_fact_is_not_equal(int seed)
    {
        // The facts rendered text left out are among them: a constant's digits, why its JSON is not known, an unknown's detail.
        foreach (var value in Sample(seed))
        {
            Value[] others =
            [
                value with { Digits = value.Digits == IntDigits.OfType ? IntDigits.Literal(1) : IntDigits.OfType },
                value with { JsonWhy = value.JsonWhy is null ? new UnknownShape(IrUnknownReason.JsonShapeUnknown, "a", Generator.Places[0]) : null, Digits = value.Digits },
                value with { IsLiveJson = !value.IsLiveJson, Digits = value.Digits },
                value with { NullableBecause = [.. value.NullableBecause, new IrNullReason(IrNullRule.Variable, "@x")], Digits = value.Digits },
                value with { Shape = Generator.Changed(value.Shape), Digits = value.Digits },
            ];
            foreach (var other in others)
                Assert.False(value == other, $"seed {seed}: {value} equals {other}");
        }
    }

    [Fact]
    public void A_join_takes_values_differing_only_in_digits_or_json_why_as_one()
    {
        // The join joins each fact in its own lattice: the digits of 1 and of 3 give the range from 1 to 10, and of two
        // values the same but for why their JSON is not known, a witness, the left one's why is kept.
        var at = new SourceLocation("p.sql", 4, 1);
        var one = new Value(ScalarShape.FromIr(new IrScalar(IrScalarKind.Number, IrFormat.Int32, "int")), []) { Digits = IntDigits.Literal(1) };
        var three = one with { Digits = IntDigits.Literal(3) };

        Assert.Equal(one with { Digits = IntDigits.Unknown }, Value.Join(one, three, at, "@n"));

        var text = new Value(ScalarShape.FromIr(new IrScalar(IrScalarKind.String, null, "nvarchar(max)")), []);
        var first = text with { JsonWhy = new UnknownShape(IrUnknownReason.JsonShapeUnknown, "first", at) };
        var second = text with { JsonWhy = new UnknownShape(IrUnknownReason.JsonShapeUnknown, "second", at) };

        Assert.Equal(first, Value.Join(first, second, at, "@t"));
    }

    [Fact]
    public void An_unknown_with_another_detail_is_another_value()
    {
        // The detail is what a reader is told, so two unknowns that say different things are not the same unknown.
        var at = new SourceLocation("p.sql", 3, 1);

        Assert.NotEqual(
            new UnknownShape(IrUnknownReason.UnsupportedExpression, "@a holds values of different shapes", at),
            new UnknownShape(IrUnknownReason.UnsupportedExpression, "@b holds values of different shapes", at));
    }

    [Fact]
    public void A_recursive_shape_names_its_function_as_sql_server_does_ignoring_case()
    {
        var tree = new RecursiveShape(new ObjectName("dbo", "tree"));
        var same = new RecursiveShape(new ObjectName("DBO", "Tree"));

        Assert.Equal(tree, same);
        Assert.Equal(tree.GetHashCode(), same.GetHashCode());
    }

    [Fact]
    public void States_with_the_same_variables_are_equal_whatever_the_order_or_case_of_their_names()
    {
        var number = new Value(ScalarShape.FromIr(new IrScalar(IrScalarKind.Number, IrFormat.Int32, "int")), []);
        var text = new Value(ScalarShape.FromIr(new IrScalar(IrScalarKind.String, null, "nvarchar(10)")), [new IrNullReason(IrNullRule.Variable, "@name")]);

        var a = FlowState.Start([new("@id", number), new("@name", text)]);
        var b = FlowState.Start([new("@NAME", text with { NullableBecause = [new IrNullReason(IrNullRule.Variable, "@name")] }), new("@Id", number)]);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, a.WithVariable("@id", text));
    }

    [Fact]
    public void Call_contexts_compare_their_tables_by_value()
    {
        // The call cache is keyed by context: a context built again from equal tables must find the walk already made.
        var column = new TempColumn("qty", new Value(ScalarShape.FromIr(new IrScalar(IrScalarKind.Number, IrFormat.Int32, "int")), []), null);
        CallContext Context(string name) => CallContext.Top with { Temps = ImmutableDictionary.Create<string, TempTable>(StringComparer.OrdinalIgnoreCase).Add(name, new KnownTable("#lines", [column with { }])) };

        Assert.Equal(Context("#lines"), Context("#LINES"));
        Assert.Equal(Context("#lines").GetHashCode(), Context("#LINES").GetHashCode());
        Assert.NotEqual(Context("#lines"), Context("#lines") with { Escapes = true });
        Assert.NotEqual(Context("#lines"), CallContext.Top);
    }

    /// <summary>
    /// Values and shapes drawn at random from small alphabets, every list and record built new, so that equal values are
    /// never the same objects.
    /// </summary>
#pragma warning disable CA5394 // A seeded Random gives the same sample on every run, which is the point; nothing here is secret.
    private sealed class Generator(Random random)
    {
        public static readonly SourceLocation[] Places = [new("p.sql", 1, 1), new("p.sql", 2, 5)];

        private static readonly IrNullReason[] Reasons =
            [new(IrNullRule.NullableColumn, "dbo.t.a"), new(IrNullRule.UnmatchedRow, "p.sql:2:5"), new(IrNullRule.NoRows, "p.sql:1:1")];

        public Value Value(int depth) => new(
            Shape(depth),
            ReasonList(),
            IsLiveJson: random.Next(4) == 0,
            JsonWhy: random.Next(4) == 0 ? Unknown() : null)
        {
            Digits = random.Next(3) switch
            {
                0 => IntDigits.Literal(random.Next(1, 3)),
                1 => IntDigits.Join(IntDigits.Literal(2), IntDigits.Literal(3)),
                _ => IntDigits.OfType,
            },
        };

        public Shape Shape(int depth) => random.Next(depth == 0 ? 4 : 10) switch
        {
            0 or 1 => ScalarShape.FromIr(Scalar()),
            2 => Unknown(),
            3 => random.Next(2) == 0 ? new NeverShape(Pick(Places)) : new RecursiveShape(new ObjectName("dbo", random.Next(2) == 0 ? "tree" : "Tree")),
            4 => new ArrayShape(Shape(depth - 1), ReasonList(), random.Next(2) == 0 ? null : 1, null),
            5 => new TupleShape([.. Enumerable.Range(0, random.Next(3)).Select(_ => new ElementShape(Shape(depth - 1), ReasonList()))]),
            6 => new ObjectShape([.. Enumerable.Range(0, random.Next(1, 3)).Select(i => new FieldShape(i == 0 ? "id" : "name", Shape(depth - 1), random.Next(2) == 0 ? IrPresence.Always : IrPresence.OmittedWhenNull, ReasonList()))]),
            7 => new UnionShape([Shape(depth - 1), Shape(depth - 1)]),
            8 => new RecordShape(Shape(depth - 1), ReasonList()),
            _ => new JsonTextShape(random.Next(2) == 0 ? null : Shape(depth - 1)),
        };

        /// <summary>The shape with one fact changed, at the first place a walk down its first children reaches.</summary>
        public static Shape Changed(Shape shape) => shape switch
        {
            ScalarShape scalar => ScalarShape.FromIr(scalar.Type with { Sql = scalar.Type.Sql + " changed" }),
            UnknownShape unknown => unknown with { Detail = unknown.Detail + " changed" },
            NeverShape never => never with { At = never.At with { Line = never.At.Line + 1 } },
            RecursiveShape => new RecursiveShape(new ObjectName("dbo", "other")),
            ArrayShape array => array with { Items = Changed(array.Items) },
            TupleShape { Items.Count: 0 } => new TupleShape([new ElementShape(new NeverShape(Places[0]), [])]),
            TupleShape tuple => new TupleShape([tuple.Items[0] with { Type = Changed(tuple.Items[0].Type) }, .. tuple.Items.Skip(1)]),
            ObjectShape obj => new ObjectShape([obj.Fields[0] with { Type = Changed(obj.Fields[0].Type) }, .. obj.Fields.Skip(1)]),
            UnionShape union => new UnionShape([.. union.Variants.SkipLast(1), Changed(union.Variants[^1])]),
            RecordShape record => record with { Values = Changed(record.Values) },
            JsonTextShape { Content: null } => new JsonTextShape(new NeverShape(Places[0])),
            JsonTextShape text => new JsonTextShape(Changed(text.Content)),
        };

        private IrScalar Scalar() => random.Next(4) switch
        {
            0 => new IrScalar(IrScalarKind.Number, IrFormat.Int32, "int"),
            1 => new IrScalar(IrScalarKind.String, null, "nvarchar(10)", new IrFacets(Length: new IrLength(10, false, IrLengthUnit.Utf16, IrByteEncoding.Unknown, new IrFactSource(IrFactSourceKind.Type, "nvarchar(10)")))),
            2 => new IrScalar(IrScalarKind.Number, IrFormat.Int32, "int", new IrFacets(Values: new IrValueSet([new IrValue("1", false), new IrValue("2", false)], IrValueMatch.Exact, [new IrFactSource(IrFactSourceKind.Check, "ck_t_a")]))),
            _ => new IrScalar(IrScalarKind.Number, IrFormat.Int32, "int", new IrFacets()),
        };

        private UnknownShape Unknown() =>
            new(random.Next(2) == 0 ? IrUnknownReason.UnsupportedQuery : IrUnknownReason.UnresolvedTable, random.Next(2) == 0 ? "a" : "b", Pick(Places));

        private EquatableArray<IrNullReason> ReasonList() => random.Next(3) switch
        {
            0 => [],
            1 => [Pick(Reasons) with { }],
            _ => [Reasons[0] with { }, Reasons[1] with { }],
        };

        private T Pick<T>(T[] items) => items[random.Next(items.Length)];
    }
#pragma warning restore CA5394
}
