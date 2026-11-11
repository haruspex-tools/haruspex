namespace Haruspex.Ir;

/// <summary>What a length counts.</summary>
public enum IrLengthUnit
{
    /// <summary>UTF-16 code units, as JavaScript's <c>length</c> counts them.</summary>
    Utf16,

    /// <summary>Bytes of the stored encoding (see <see cref="IrLength.Encoding"/>).</summary>
    Bytes,
}
