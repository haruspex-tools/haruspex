using Haruspex.Ir;

namespace Haruspex.Tests.Emit.JsonSchema;

/// <summary>Any JSON (0.28) in JSON Schema: <c>{}</c>, which accepts every JSON value, or narrowed, an object or an array.</summary>
public partial class JsonSchemaEmitterTests
{
    [Fact]
    public void Any_json_accepts_every_json_value()
    {
        AssertSchema("{}", Value(new IrAnyJson(), IrEncoding.Json));
        Assert.Contains("Any JSON value", Description(Value(new IrAnyJson(), IrEncoding.Json)), StringComparison.Ordinal);
    }

    [Fact]
    public void Any_json_narrowed_is_an_object_or_an_array() =>
        AssertSchema("""{ "type": ["object", "array"] }""", Value(new IrAnyJson(ObjectOrArray: true), IrEncoding.Json));
}
