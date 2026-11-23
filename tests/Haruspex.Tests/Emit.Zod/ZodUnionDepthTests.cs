using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests.Emit.Zod;

/// <summary>
/// A union's variants told apart at every depth. <c>z.union</c> takes the first variant that accepts a value, and a <c>z.object</c>
/// strips the keys it doesn't know, so two variants with the same keys whose nested objects differ would let the first accept the
/// other's value and drop its nested keys. Each variant refuses, at each path, the keys only the others' objects have there.
/// </summary>
public class ZodUnionDepthTests
{
    private static readonly IrScalar Int = new(IrScalarKind.Number, IrFormat.Int32, "int");

    private static readonly IrNullReason NoRows = new(IrNullRule.EmptySubquery, "p.sql:3:9");

    private static IrField Key(string name, IrType type) => new(name, type, IrPresence.Always, []);

    private static IrObjectType Object(params IrField[] fields) => new([.. fields]);

    private static string Emit(IrType type, Dictionary<string, IrObjectType> types)
    {
        types["PRow"] = Object(Key("v", type));
        return ZodEmitter.Emit(new IrDocument(
            new IrSource("tsql"),
            [new IrObject("s.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(IrEncoding.Json, new IrArray(new IrRef("PRow")), [])])],
            types,
            [])).Expected();
    }

    /// <summary>The same keys at the top, and a nested list of sales whose objects have an amount in one variant only.</summary>
    private static Dictionary<string, IrObjectType> BriefAndFull() => new()
    {
        ["Brief"] = Object(Key("id", Int), new IrField("sales", new IrArray(new IrRef("BriefSale")), IrPresence.OmittedWhenNull, [NoRows])),
        ["Full"] = Object(Key("id", Int), new IrField("sales", new IrArray(new IrRef("FullSale")), IrPresence.OmittedWhenNull, [NoRows])),
        ["BriefSale"] = Object(Key("sale_id", Int)),
        ["FullSale"] = Object(Key("sale_id", Int), Key("amount", Int)),
    };

    [Theory]
    [InlineData("Brief", "Full")]
    [InlineData("Full", "Brief")]
    public void Variants_with_the_same_keys_refuse_the_keys_only_the_other_s_nested_objects_have(string first, string second)
    {
        var ts = Emit(new IrUnion([new IrRef(first), new IrRef(second)]), BriefAndFull());

        // The field keeps its presence: a FOR JSON subquery of no rows leaves the key out.
        const string Brief = "BriefSchema.extend({ sales: z.array(BriefSaleSchema.extend({ amount: z.never().optional() })).optional() })";
        Assert.Contains(first == "Brief" ? $"v: z.union([{Brief}, FullSchema])," : $"v: z.union([FullSchema, {Brief}]),", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void Arrays_of_variants_refuse_nested_keys_inside_their_items()
    {
        var ts = Emit(new IrUnion([new IrArray(new IrRef("Brief")), new IrArray(new IrRef("Full"))]), BriefAndFull());

        Assert.Contains(
            "v: z.union([z.array(BriefSchema.extend({ sales: z.array(BriefSaleSchema.extend({ amount: z.never().optional() })).optional() })), z.array(FullSchema)]),",
            ts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Keys_are_refused_through_a_nested_object_and_a_nullable_list_at_any_depth()
    {
        var types = new Dictionary<string, IrObjectType>
        {
            ["Outer"] = Object(Key("a", new IrRef("Mid"))),
            ["Outer2"] = Object(Key("a", new IrRef("Mid2"))),
            ["Mid"] = Object(Key("b", new IrArray(new IrRef("Leaf")) { ItemsNullableBecause = [NoRows] })),
            ["Mid2"] = Object(Key("b", new IrArray(new IrRef("Leaf2")) { ItemsNullableBecause = [NoRows], MinItems = 1 })),
            ["Leaf"] = Object(Key("k", Int)),
            ["Leaf2"] = Object(Key("k", Int), Key("m", Int)),
        };

        var ts = Emit(new IrUnion([new IrRef("Outer"), new IrRef("Outer2")]), types);

        Assert.Contains(
            "v: z.union([OuterSchema.extend({ a: MidSchema.extend({ b: z.array(LeafSchema.extend({ m: z.never().optional() }).nullable()) }) }), Outer2Schema]),",
            ts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Variants_whose_nested_objects_are_one_type_are_left_as_they_are()
    {
        var types = BriefAndFull();
        types["Full"] = types["Brief"] with { Fields = [.. types["Brief"].Fields, Key("note", Int)] };

        var ts = Emit(new IrUnion([new IrRef("Brief"), new IrRef("Full")]), types);

        Assert.Contains("v: z.union([FullSchema, BriefSchema.extend({ note: z.never().optional() })]),", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void Recursive_variants_refuse_keys_once_round_each_cycle_and_end()
    {
        var types = new Dictionary<string, IrObjectType>
        {
            ["Tree"] = Object(Key("kids", new IrArray(new IrRef("Tree")))) with { Recursive = true },
            ["Tree2"] = Object(Key("kids", new IrArray(new IrRef("Tree2"))), Key("extra", Int)) with { Recursive = true },
        };

        var ts = Emit(new IrUnion([new IrRef("Tree"), new IrRef("Tree2")]), types);

        Assert.Contains("v: z.union([Tree2Schema, TreeSchema.extend({ extra: z.never().optional() })]),", ts, StringComparison.Ordinal);
    }
}
