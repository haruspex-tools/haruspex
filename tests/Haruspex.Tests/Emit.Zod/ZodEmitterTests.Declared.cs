using System.Text.RegularExpressions;
using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests.Emit.Zod;

/// <summary>
/// What a declaration gives (0.28) in Zod: any JSON, <c>z.json()</c>, which the pinned Zod observes accepting every JSON value and
/// nothing else; and a maximum length in code points, which Zod's own check counts (both observed in <c>ZodObservationTests</c>).
/// </summary>
public partial class ZodEmitterTests
{
    [Theory]
    [InlineData(false, "z.json()")]
    [InlineData(true, "z.union([z.record(z.string(), z.json()), z.array(z.json())])")]
    public void Any_json_is_z_json_and_narrowed_a_union_of_any_object_and_any_array(bool objectOrArray, string zod)
    {
        var ts = ZodEmitter.Emit(OneResult(IrEncoding.Json, Field("v", new IrAnyJson(objectOrArray)))).Expected();

        Assert.Matches($@"\n  v: {Regex.Escape(zod)}( /\*[^\n]*\*/)?,\n", ts);
    }

    [Fact]
    public void Text_holding_any_json_is_parsed_and_checked_as_any_json()
    {
        var ts = ZodEmitter.Emit(OneResult(IrEncoding.Rowset, Field("v", new IrJsonText(new IrAnyJson())))).Expected();

        Assert.Contains("jsonText(() => z.json())", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void A_maximum_length_in_code_points_is_z_max_which_counts_them()
    {
        var text = new IrScalar(IrScalarKind.String, null, null, new IrFacets(Length: new IrLength(128, IsFixed: false, IrLengthUnit.CodePoints, IrByteEncoding.Unknown, new IrFactSource(IrFactSourceKind.Declared, "shop.orders.settings"))));

        var ts = ZodEmitter.Emit(OneResult(IrEncoding.Json, Field("v", text))).Expected();

        Assert.Matches(@"\n  v: z\.string\(\)\.max\(128\)( /\*[^\n]*\*/)?,\n", ts);
    }

    [Fact]
    public void An_exact_set_of_numbers_of_no_format_is_z_literal_in_json()
    {
        var levels = new IrScalar(IrScalarKind.Number, null, null, new IrFacets(Values: new IrValueSet([new IrValue("1", IsString: false), new IrValue("2.5", IsString: false)], IrValueMatch.Exact, [new IrFactSource(IrFactSourceKind.Declared, "shop.orders.settings")])));

        var ts = ZodEmitter.Emit(OneResult(IrEncoding.Json, Field("v", levels))).Expected();

        Assert.Matches(@"\n  v: z\.literal\(\[1, 2\.5\]\)( /\*[^\n]*\*/)?,\n", ts);
    }
}
