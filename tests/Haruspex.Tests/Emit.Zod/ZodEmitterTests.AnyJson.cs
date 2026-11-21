using System.Text.RegularExpressions;
using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests.Emit.Zod;

/// <summary>Any JSON (0.28) in Zod: <c>z.json()</c>, which the pinned Zod observes accepting every JSON value and nothing else.</summary>
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
}
