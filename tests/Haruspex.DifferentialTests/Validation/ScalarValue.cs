using System.Data.SqlTypes;
using System.Globalization;
using System.Text.Json;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests.Validation;

/// <summary>A non-null scalar as the facets check it: a string's text, or a number's exact value.</summary>
internal sealed record ScalarValue(string? Text, Exact? Number)
{
    /// <summary>From JSON; null for kinds that carry no facets (dates, GUIDs, booleans).</summary>
    public static ScalarValue? FromJson(IrScalar scalar, JsonElement value) => scalar.Kind switch
    {
        IrScalarKind.String => new ScalarValue(value.GetString(), null),
        IrScalarKind.Number => Exact.TryParse(value.GetRawText()) is { } n ? new ScalarValue(null, n) : null,
        _ => null,
    };

    /// <summary>From a value SqlClient read; null for other types.</summary>
    public static ScalarValue? FromRowset(object value) => value switch
    {
        string s => new ScalarValue(s, null),
        byte or short or int or long or decimal => new ScalarValue(null, Exact.Parse(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture))),
        SqlDecimal d => new ScalarValue(null, Exact.Parse(d.ToString())),
        double d => new ScalarValue(null, Exact.Parse(d.ToString("R", CultureInfo.InvariantCulture))),
        float f => new ScalarValue(null, Exact.Parse(((double)f).ToString("R", CultureInfo.InvariantCulture))),
        _ => null,
    };

    public string Describe() => Text is not null ? JsonSerializer.Serialize(Text) : Number!.Value.ToString();
}
