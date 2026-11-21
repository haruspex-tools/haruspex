using Haruspex.Ir;

namespace Haruspex.Tests.Emit.JsonSchema;

/// <summary>
/// What a declaration gives (0.28) in JSON Schema: any JSON, <c>{}</c>, which accepts every JSON value, or narrowed, an object or an
/// array; and a maximum length in code points, which JSON Schema counts too.
/// </summary>
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
        AssertSchema("""{ "anyOf": [{ "type": "object" }, { "type": "array" }] }""", Value(new IrAnyJson(ObjectOrArray: true), IrEncoding.Json));

    [Fact]
    public void A_maximum_length_in_code_points_is_max_length_exactly() =>
        AssertSchema(
            """{ "type": "string", "maxLength": 128 }""",
            Value(new IrScalar(IrScalarKind.String, null, null, new IrFacets(Length: new IrLength(128, IsFixed: false, IrLengthUnit.CodePoints, IrByteEncoding.Unknown, new IrFactSource(IrFactSourceKind.Declared, "shop.orders.settings")))), IrEncoding.Json));
}
