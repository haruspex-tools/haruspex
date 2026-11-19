using System.Collections.Frozen;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// The functions that make a binary value of another: HASHBYTES, COMPRESS and DECOMPRESS. Each is typed for text or binary
// arguments only; README.md in this folder ("Binary values") gives the rules, observed in BinaryObservationTests.
internal sealed partial class Typer
{
    /// <summary>
    /// The bytes of each algorithm's digest that <c>HASHBYTES</c> runs at every compatibility level from 100 to 170, by its name in
    /// upper case (observed: Hashbytes_is_varbinary_8000_of_its_algorithms_length_and_null_for_an_algorithm_it_does_not_run).
    /// MD2 is not one: it gives NULL at all of them.
    /// </summary>
    private static readonly FrozenDictionary<string, int> Digests = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["MD4"] = 16,
        ["MD5"] = 16,
        ["SHA"] = 20,
        ["SHA1"] = 20,
        ["SHA2_256"] = 32,
        ["SHA2_512"] = 64,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// <c>HASHBYTES(algorithm, x)</c> of text or a binary value: <c>varbinary(8000)</c>, as the server describes it whatever the
    /// algorithm, NULL when x is. An algorithm written as a literal that names one it runs, in any case and with trailing spaces, gives
    /// a digest of exactly that many bytes, a fact of the engine's; any other may give NULL (<c>out-of-range</c>): MD2, a name it
    /// doesn't know, one with a leading space, and one that isn't a literal (observed in the test <see cref="Digests"/> names).
    /// </summary>
    private Value HashBytes(FunctionCall call, ReadScope scope)
    {
        var (values, unknown) = Arguments(call.Parameters, scope);
        if (unknown is not null)
            return unknown;
        if (StringOf(values[0]) is null || TypeOf(values[1]) is not ({ IsString: true } or { IsBinary: true }))
            return NotModelled(call, "HASHBYTES of a value that is neither text nor binary, or by an algorithm that is not text, is not modelled.");

        var named = LiteralOf(call.Parameters[0]) is ({ } literal and StringLiteral, false) ? literal.Value.TrimEnd(' ').ToUpperInvariant() : null;
        if (named is null || !Digests.TryGetValue(named, out var bytes))
            return Typed(new ScalarType(BuiltInType.VarBinary, BuiltInType.MostBytes), NullReasons.Join(values[1].NullableBecause, [OutOfRangeAt(call)]), call);

        var value = Typed(new ScalarType(BuiltInType.VarBinary, BuiltInType.MostBytes), values[1].NullableBecause, call);
        if (value.Shape is not ScalarShape scalar)
            return value;
        var digest = new IrLength(bytes, IsFixed: true, IrLengthUnit.Bytes, IrByteEncoding.Unknown, new IrFactSource(IrFactSourceKind.Engine, $"HASHBYTES with {named}"));
        return value with { Shape = scalar with { Type = scalar.Type with { Facets = (scalar.Type.Facets ?? new IrFacets()) with { Length = digest } } } };
    }

    /// <summary>
    /// <c>COMPRESS(x)</c> of text or a binary value, and <c>DECOMPRESS(x)</c> of a binary value: <c>varbinary(max)</c>, NULL only
    /// when x is. Bytes <c>DECOMPRESS</c> can't read are an error, not NULL, under any options (observed:
    /// Compress_and_decompress_are_varbinary_max_null_only_for_null_and_decompress_of_other_bytes_is_an_error).
    /// </summary>
    private Value Compression(FunctionCall call, ReadScope scope, bool decompress)
    {
        var value = Expression(call.Parameters[0], scope);
        if (value.Shape is UnknownShape)
            return value;
        var type = value.IsLiveJson ? null : TypeOf(value);
        if (type is null || !(type.IsBinary || (type.IsString && !decompress)))
            return NotModelled(call, decompress ? "DECOMPRESS of a value that is not binary is not modelled." : "COMPRESS of a value that is neither text nor binary is not modelled.");
        return Typed(new ScalarType(BuiltInType.VarBinary, -1), value.NullableBecause, call);
    }
}
