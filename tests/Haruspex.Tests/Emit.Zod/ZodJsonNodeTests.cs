using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests.Emit.Zod;

/// <summary>How the Zod emitter writes the nodes the JSON constructors produce.</summary>
public class ZodJsonNodeTests
{
    private static readonly IrScalar Int = new(IrScalarKind.Number, IrFormats.Int32, "int");
    private static readonly IrScalar Text = new(IrScalarKind.String, null, "nvarchar(10)");
    private static readonly IrNullReason Because = new(IrNullRules.NullableColumn, "a.t.c");

    private static string Emit(IrType type, Dictionary<string, IrObjectType>? more = null)
    {
        var types = new Dictionary<string, IrObjectType> { ["PRow"] = new([new IrField("v", type, IrPresence.Always, [])]) };
        foreach (var (name, t) in more ?? [])
            types[name] = t;
        return ZodEmitter.Emit(new IrDocument(
            new IrSource("tsql"),
            [new IrObject("s.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(IrEncoding.Json, new IrArray(new IrRef("PRow")), [])])],
            types,
            []));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Writes_each_node_as_zod(IrType type, string zod) =>
        Assert.Contains($"\n  v: {zod},\n", Emit(type), StringComparison.Ordinal);

    public static TheoryData<IrType, string> Cases { get; } = new()
    {
        { new IrTuple([new IrElement(Int, []), new IrElement(Text, [Because])]), "z.tuple([z.int32(), z.string().nullable()])" },
        { new IrTuple([]), "z.tuple([])" },
        { new IrArray(Int) { ItemsNullableBecause = [Because] }, "z.array(z.int32().nullable())" },
        { new IrArray(Int) { MinItems = 1 }, "z.array(z.int32()).min(1)" },
        { new IrArray(new IrUnion([Int, Text])) { MinItems = 1, MaxItems = 3 }, "z.array(z.union([z.int32(), z.string()])).min(1).max(3)" },
        { new IrRecord(Int, []), "z.record(z.string(), z.int32())" },
        { new IrRecord(Text, [Because]), "z.record(z.string(), z.string().nullable())" },
        { new IrUnion([Int, Text]), "z.union([z.int32(), z.string()])" },
    };

    [Fact]
    public void A_union_with_an_unknown_variant_keeps_the_unknown_and_says_why() =>
        Assert.Contains(
            "\n  v: z.union([z.int32(), z.unknown()]) /* unsupported-expression */,\n",
            Emit(new IrUnion([Int, new IrUnknownType(IrUnknownReasons.UnsupportedExpression)])),
            StringComparison.Ordinal);

    [Fact]
    public void Writes_object_types_reached_through_tuples_records_and_unions_first()
    {
        var more = new Dictionary<string, IrObjectType>
        {
            ["A"] = new([new IrField("a", Int, IrPresence.Always, [])]),
            ["B"] = new([new IrField("b", Int, IrPresence.Always, [])]),
            ["C"] = new([new IrField("c", Int, IrPresence.Always, [])]),
        };

        var ts = Emit(new IrTuple([new IrElement(new IrRecord(new IrRef("A"), []), []), new IrElement(new IrUnion([new IrRef("B"), new IrRef("C")]), [])]), more);

        var row = ts.IndexOf("export const PRowSchema", StringComparison.Ordinal);
        Assert.All(["ASchema =", "BSchema =", "CSchema ="], s => Assert.InRange(ts.IndexOf(s, StringComparison.Ordinal), 0, row));
        // Each object variant rejects the keys only the other has, so neither strips the other's.
        Assert.Contains("v: z.tuple([z.record(z.string(), ASchema), z.union([BSchema.extend({ c: z.never().optional() }), CSchema.extend({ b: z.never().optional() })])]),", ts, StringComparison.Ordinal);
    }
}
