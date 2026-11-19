using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>The functions that make a binary value: <c>HASHBYTES</c>, <c>COMPRESS</c> and <c>DECOMPRESS</c>.</summary>
public partial class BinaryTests
{
    [Theory]
    // Hashbytes_is_varbinary_8000_of_its_algorithms_length_and_null_for_an_algorithm_it_does_not_run, at every compatibility
    // level from 100 to 170: MD4 and MD5 16 bytes, SHA and SHA1 20, SHA2_256 32, SHA2_512 64, the name in any case and trailing
    // spaces ignored.
    [InlineData("'SHA2_256'", 32)]
    [InlineData("'sha2_512'", 64)]
    [InlineData("'MD5'", 16)]
    [InlineData("'MD4'", 16)]
    [InlineData("'SHA'", 20)]
    [InlineData("'SHA1 '", 20)]
    [InlineData("N'SHA2_256'", 32)]
    public void Hashbytes_is_exactly_as_long_as_its_algorithm_s_digest(string algorithm, int length)
    {
        var doc = Procedure($"SELECT HASHBYTES({algorithm}, code) AS h, HASHBYTES({algorithm}, checksum) AS b FROM shop.product_image;");

        Assert.Equal($"varbinary(8000) exactly {length}", Typed(doc, "h"));
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "h"));
        Assert.Empty(Field(doc, "b").NullableBecause);
        var from = ((IrScalar)Field(doc, "h").Type).Facets!.Length!.From;
        Assert.Equal(IrFactSourceKind.Engine, from.Kind);
    }

    [Theory]
    // MD2 gives NULL, and so does a name it doesn't know, one with a leading space, and one not written as a literal.
    [InlineData("'MD2'")]
    [InlineData("'SHA3_256'")]
    [InlineData("' MD5'")]
    [InlineData("code")]
    public void Hashbytes_of_an_algorithm_it_may_not_run_is_varbinary_8000_and_may_be_null(string algorithm)
    {
        var doc = Procedure($"SELECT HASHBYTES({algorithm}, checksum) AS h FROM shop.product_image;");

        Assert.Equal("varbinary(8000) at most 8000", Typed(doc, "h"));
        Assert.Contains(IrNullRule.OutOfRange, Rules(doc, "h"));
    }

    [Theory]
    [InlineData("HASHBYTES('MD5', image_id)")]
    [InlineData("HASHBYTES('MD5', scan)")]
    public void Hashbytes_of_a_value_that_is_neither_text_nor_binary_is_not_modelled(string expression) =>
        Assert.StartsWith("unknown", Typed(Procedure($"SELECT {expression} AS v FROM shop.product_image;"), "v"), StringComparison.Ordinal);

    [Fact]
    public void Compress_and_decompress_are_varbinary_max_and_null_only_for_a_null_argument()
    {
        // Compress_and_decompress_are_varbinary_max_null_only_for_null_and_decompress_of_other_bytes_is_an_error.
        var doc = Procedure("""
            SELECT COMPRESS(code) AS cs, COMPRESS(checksum) AS cb, DECOMPRESS(thumbnail) AS d, DECOMPRESS(COMPRESS(checksum)) AS round_trip
            FROM shop.product_image;
            """);

        Assert.All((string[])["cs", "cb", "d", "round_trip"], f => Assert.Equal("varbinary(max)", Typed(doc, f)));
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "cs"));
        Assert.Empty(Field(doc, "cb").NullableBecause);
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "d"));
        Assert.Empty(Field(doc, "round_trip").NullableBecause);
    }

    [Theory]
    [InlineData("COMPRESS(image_id)")]
    [InlineData("DECOMPRESS(code)")]
    [InlineData("COMPRESS(scan)")]
    public void Compress_or_decompress_of_a_value_of_another_kind_is_not_modelled(string expression) =>
        Assert.StartsWith("unknown", Typed(Procedure($"SELECT {expression} AS v FROM shop.product_image;"), "v"), StringComparison.Ordinal);
}
