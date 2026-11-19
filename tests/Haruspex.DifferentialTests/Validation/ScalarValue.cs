using System.Data.SqlTypes;
using System.Globalization;
using System.Text.Json;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests.Validation;

/// <summary>A non-null scalar as the facets check it: a string's text, a number's exact value, or a binary value's bytes.</summary>
internal sealed record ScalarValue(string? Text, Exact? Number, byte[]? Bytes = null)
{
    /// <summary>From JSON; null for kinds that carry no facets (dates, GUIDs, booleans). A binary value is its base64 text decoded.</summary>
    public static ScalarValue? FromJson(IrScalar scalar, JsonElement value) => scalar.Kind switch
    {
        IrScalarKind.String => new ScalarValue(value.GetString(), null),
        IrScalarKind.Number => Exact.TryParse(value.GetRawText()) is { } n ? new ScalarValue(null, n) : null,
        IrScalarKind.Bytes => new ScalarValue(null, null, Convert.FromBase64String(value.GetString()!)),
        _ => null,
    };

    /// <summary>From a value SqlClient read; null for other types.</summary>
    public static ScalarValue? FromRowset(object value) => value switch
    {
        string s => new ScalarValue(s, null),
        byte[] bytes => new ScalarValue(null, null, bytes),
        byte or short or int or long or decimal => new ScalarValue(null, Exact.Parse(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture))),
        SqlDecimal d => new ScalarValue(null, Exact.Parse(d.ToString())),
        double d => new ScalarValue(null, Exact.Parse(d.ToString("R", CultureInfo.InvariantCulture))),
        float f => new ScalarValue(null, Exact.Parse(((double)f).ToString("R", CultureInfo.InvariantCulture))),
        _ => null,
    };

    public string Describe() => Text is not null ? JsonSerializer.Serialize(Text) : Bytes is not null ? $"0x{Convert.ToHexString(Bytes)}" : Number!.Value.ToString();
}
