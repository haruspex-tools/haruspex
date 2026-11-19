using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// Binary values (decision 0081): the types that hold them, the literals and the expressions that make them, each a scalar of kind
/// <c>bytes</c> with its length in bytes. Each type, length and reason to be null rests on an observation in the differential tests'
/// <c>BinaryObservationTests</c>, named where it matters. The functions that make binary values (<c>HASHBYTES</c>,
/// <c>COMPRESS</c> and <c>DECOMPRESS</c>) are in <c>BinaryTests.Functions.cs</c>.
/// </summary>
public partial class BinaryTests
{
    private const string Tables = """
        CREATE TABLE shop.product_image (
            image_id int NOT NULL PRIMARY KEY, thumbnail varbinary(8000) NULL, checksum binary(32) NOT NULL, row_version rowversion,
            original varbinary(max) NULL, scan image NULL, caption nvarchar(40) NULL, code varchar(10) NULL);
        GO
        """;

    private static IrDocument Procedure(string body) =>
        InferProcedure.Infer("shop.p", ("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE shop.p AS\n{body}"));

    private static IrDocument Procedure(string parameters, string body) =>
        InferProcedure.Infer("shop.p", ("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE shop.p {parameters} AS\n{body}"));

    /// <summary>The field's SQL type and its length in bytes (<c>varbinary(8000) at most 8000</c>), or <c>unknown</c> with the reason's detail.</summary>
    private static string Typed(IrDocument doc, string name) => Field(doc, name).Type switch
    {
        IrScalar { Kind: IrScalarKind.Bytes } scalar => scalar.Sql + (scalar.Facets?.Length is { } length ? (length.IsFixed ? " exactly " : " at most ") + length.Value : ""),
        IrScalar scalar => $"{IrWireNames.Of(scalar.Kind)} {scalar.Sql}",
        IrUnknownType => "unknown: " + string.Join("; ", doc.Unknowns.Select(u => u.Detail)),
        var other => other.ToString(),
    };

    // ---- the types -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Each_binary_type_is_bytes_of_its_length_and_a_rowversion_is_a_timestamp_of_eight()
    {
        var doc = Procedure("SELECT thumbnail, checksum, row_version, original FROM shop.product_image;");

        Assert.Equal("varbinary(8000) at most 8000", Typed(doc, "thumbnail"));
        Assert.Equal("binary(32) exactly 32", Typed(doc, "checksum"));
        Assert.Equal("timestamp exactly 8", Typed(doc, "row_version"));
        Assert.Equal("varbinary(max)", Typed(doc, "original"));
        Assert.Empty(Field(doc, "checksum").NullableBecause);
        Assert.Empty(Field(doc, "row_version").NullableBecause);
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "thumbnail"));
        Assert.Equal(IrDocument.CurrentVersion, doc.Version);
    }

    [Fact]
    public void A_nullable_binary_column_or_one_a_body_makes_is_at_most_its_length_but_a_table_variable_s_pads()
    {
        // A_nullable_binary_column_created_with_ansi_padding_off_cuts_trailing_zeros_and_a_not_null_one_pads: the source doesn't say
        // which tables were created with ANSI_PADDING off, nor which caller's session a temporary table is made in. A table
        // variable's column pads whatever the setting.
        var doc = Procedure("""
            CREATE TABLE #c (stamp binary(4) NULL, kept binary(4) NOT NULL);
            DECLARE @v TABLE (stamp binary(4) NULL);
            SELECT checksum AS made INTO #t FROM shop.product_image;
            SELECT p.checksum, c.stamp, c.kept, v.stamp AS variable_stamp, t.made
            FROM shop.product_image AS p CROSS JOIN #c AS c CROSS JOIN @v AS v CROSS JOIN #t AS t;
            """);

        Assert.Equal("binary(32) exactly 32", Typed(doc, "checksum"));
        Assert.Equal("binary(4) at most 4", Typed(doc, "stamp"));
        Assert.Equal("binary(4) exactly 4", Typed(doc, "kept"));
        Assert.Equal("binary(4) exactly 4", Typed(doc, "variable_stamp"));
        Assert.Equal("binary(32) at most 32", Typed(doc, "made"));
    }

    [Fact]
    public void An_image_column_is_bytes_of_no_length()
    {
        // An_image_value_arrives_as_varbinary_max_s_does: a Buffer from the driver, base64 in FOR JSON.
        var doc = Procedure("SELECT scan FROM shop.product_image;");

        Assert.Equal("image", Typed(doc, "scan"));
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "scan"));
    }

    [Fact]
    public void A_binary_value_s_length_counts_bytes_with_no_encoding()
    {
        var length = ((IrScalar)Field(Procedure("SELECT checksum FROM shop.product_image;"), "checksum").Type).Facets!.Length!;

        Assert.Equal(new IrLength(32, IsFixed: true, IrLengthUnit.Bytes, IrByteEncoding.Unknown, new IrFactSource(IrFactSourceKind.Type, "binary(32)")), length);
    }

    [Fact]
    public void A_binary_parameter_is_bytes_of_its_declared_length()
    {
        var doc = Procedure("@thumbnail varbinary(8000), @checksum binary(32), @scan image, @version rowversion", "SELECT 1 AS one;");
        var types = doc.Objects[0].Parameters.Select(p => p.Type).Cast<IrScalar>().ToList();

        Assert.All(types, t => Assert.Equal(IrScalarKind.Bytes, t.Kind));
        Assert.Equal([8000, 32, null, 8], types.Select(t => t.Facets?.Length?.Value));
        Assert.Equal([false, true, null, true], types.Select(t => t.Facets?.Length?.IsFixed));
    }

    [Fact]
    public void A_binary_column_read_by_for_json_is_bytes_in_a_json_result()
    {
        var doc = Procedure("SELECT image_id, checksum, thumbnail FROM shop.product_image FOR JSON PATH;");

        Assert.Equal(IrEncoding.Json, doc.Objects[0].Results![0].Encoding);
        Assert.Equal("binary(32) exactly 32", Typed(doc, "checksum"));
        Assert.Equal(IrPresence.OmittedWhenNull, Field(doc, "thumbnail").Presence);
    }

    [Fact]
    public void A_field_read_from_a_binary_column_says_where_it_came_from_with_its_type()
    {
        var from = Assert.Single(Field(Procedure("SELECT checksum FROM shop.product_image;"), "checksum").From);

        Assert.Equal(("shop.product_image.checksum", "binary(32)"), (from.Column, from.Sql));
    }

    // ---- literals ------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("0x0A1B", "varbinary(2) at most 2")]
    [InlineData("0x", "varbinary(1) at most 1")]
    [InlineData("0xA1B", "varbinary(2) at most 2")]
    [InlineData("0X0a", "varbinary(1) at most 1")]
    [InlineData("most", "varbinary(8000) at most 8000")]
    [InlineData("past", "varbinary(max)")]
    public void A_binary_literal_is_varbinary_of_its_bytes_and_never_null(string literal, string type)
    {
        // A_binary_literal_is_varbinary_of_its_bytes: an odd digit is padded to a byte, 0x is one byte long, past 8000 is max.
        var bytes8000 = "0x" + new string('0', 16000);
        var written = literal switch { "most" => bytes8000, "past" => bytes8000 + "00", _ => literal };
        var doc = Procedure($"SELECT {written} AS b;");

        Assert.Equal(type, Typed(doc, "b"));
        Assert.Empty(Field(doc, "b").NullableBecause);
    }

    // ---- conversions ---------------------------------------------------------------------------------------------------

    [Theory]
    // A binary type without a length is 30 bytes long in a conversion, as a string is
    // (A_binary_type_without_a_length_is_30_bytes_long_in_a_conversion).
    [InlineData("CAST(image_id AS varbinary)", "varbinary(30) at most 30")]
    [InlineData("CAST(image_id AS binary)", "binary(30) exactly 30")]
    [InlineData("CONVERT(varbinary, code)", "varbinary(30) at most 30")]
    [InlineData("CAST(checksum AS varbinary(10))", "varbinary(10) at most 10")]
    [InlineData("CONVERT(varbinary(4), code, 1)", "varbinary(4) at most 4")]
    [InlineData("CONVERT(varbinary(max), code, 2)", "varbinary(max)")]
    [InlineData("CAST(scan AS varbinary(max))", "varbinary(max)")]
    [InlineData("CONVERT(varchar(66), checksum, 1)", "string varchar(66)")]
    [InlineData("CAST(row_version AS bigint)", "number bigint")]
    public void A_conversion_to_or_from_a_binary_type_is_of_the_target_type_whatever_the_style(string expression, string type) =>
        Assert.Equal(type, Typed(Procedure($"SELECT {expression} AS v FROM shop.product_image;"), "v"));

    [Fact]
    public void A_binary_value_converted_to_another_binary_type_is_cut_or_padded_never_null()
    {
        // Converting_between_binary_types_cuts_or_pads_without_an_error_or_null_under_any_options.
        var doc = Procedure("SELECT CAST(checksum AS varbinary(4)) AS cut, CAST(checksum AS binary(40)) AS padded FROM shop.product_image;");

        Assert.Empty(Field(doc, "cut").NullableBecause);
        Assert.Empty(Field(doc, "padded").NullableBecause);
    }

    [Theory]
    // Each_conversion_the_table_lists_keeps_every_value_at_the_edges_of_its_types: between binary and these kinds none is NULL.
    [InlineData("CAST(image_id AS varbinary(4))")]
    [InlineData("CAST(code AS binary(4))")]
    [InlineData("CONVERT(varbinary(4), code, 1)")]
    [InlineData("CAST(checksum AS int)")]
    [InlineData("CAST(checksum AS varchar(10))")]
    [InlineData("CAST(checksum AS uniqueidentifier)")]
    public void A_conversion_between_binary_and_another_kind_the_table_lists_is_never_null(string expression) =>
        Assert.Empty(Field(Procedure($"SELECT {expression} AS v FROM shop.product_image WHERE code IS NOT NULL;"), "v").NullableBecause);

    [Theory]
    // Each_conversion_the_table_lists_keeps_every_value_at_the_edges_of_its_types: a rowversion's 8 bytes convert to binary of any
    // length (cut or padded), an integer, bit, money or varchar without NULL or an error, under any options.
    [InlineData("CONVERT(varchar(18), CONVERT(binary(8), row_version), 1)", "string varchar(18)")]
    [InlineData("CONVERT(binary(8), row_version)", "binary(8) exactly 8")]
    [InlineData("CAST(row_version AS varbinary(4))", "varbinary(4) at most 4")]
    [InlineData("CAST(row_version AS bigint)", "number bigint")]
    [InlineData("CAST(row_version AS varchar(18))", "string varchar(18)")]
    public void A_rowversion_converted_to_binary_an_integer_or_varchar_is_never_null(string expression, string type)
    {
        var doc = Procedure($"SELECT {expression} AS v FROM shop.product_image;");

        Assert.Equal(type, Typed(doc, "v"));
        Assert.Empty(Field(doc, "v").NullableBecause);
    }

    [Theory]
    // Into a decimal or a date and time a rowversion overflows: an error, NULL under the caller's options.
    [InlineData("CAST(row_version AS decimal(20,0))")]
    [InlineData("CAST(row_version AS datetime)")]
    public void A_rowversion_converted_to_a_decimal_or_a_date_may_overflow(string expression) =>
        Assert.Contains(IrNullRule.ArithmeticOverflow, Rules(Procedure($"SELECT {expression} AS v FROM shop.product_image;"), "v"));

    [Theory]
    // Into a decimal a binary value may overflow (8115), NULL under the caller's options; text not written as hex is NULL
    // under TRY_CONVERT (Text_read_as_hex_into_binary_is_an_error_when_it_is_not_hex_and_null_under_try_convert).
    [InlineData("CAST(checksum AS decimal(10,2))", IrNullRule.ArithmeticOverflow)]
    [InlineData("TRY_CONVERT(varbinary(4), code, 1)", IrNullRule.FailedConversion)]
    // Past 8000 bytes a binary value into a number is an error (8152), which TRY_CAST isn't shown to keep from NULL.
    [InlineData("TRY_CAST(original AS int)", IrNullRule.FailedConversion)]
    public void A_conversion_that_may_fail_keeps_its_reason(string expression, IrNullRule rule) =>
        Assert.Contains(rule, Rules(Procedure($"SELECT {expression} AS v FROM shop.product_image WHERE code IS NOT NULL;"), "v"));

    // ---- combining types -----------------------------------------------------------------------------------------------

    [Theory]
    // Binary_values_combine_as_strings_do_varying_if_either_is_as_long_as_the_longer_max_if_either_is.
    [InlineData("COALESCE(thumbnail, checksum)", "varbinary(8000) at most 8000")]
    [InlineData("COALESCE(checksum, CAST(0x01 AS binary(40)))", "binary(40) exactly 40")]
    [InlineData("COALESCE(checksum, original)", "varbinary(max)")]
    [InlineData("CASE WHEN image_id = 1 THEN checksum ELSE 0x01 END", "varbinary(32) at most 32")]
    [InlineData("IIF(image_id = 1, 0x0102, checksum)", "varbinary(32) at most 32")]
    [InlineData("COALESCE(row_version, row_version)", "timestamp exactly 8")]
    public void Binary_values_combine_as_strings_do(string expression, string type) =>
        Assert.Equal(type, Typed(Procedure($"SELECT {expression} AS v FROM shop.product_image;"), "v"));

    [Theory]
    // Not modelled: a binary value beside a value of another kind takes that kind's type by rules not observed for every pair.
    [InlineData("COALESCE(thumbnail, code)")]
    [InlineData("COALESCE(checksum, image_id)")]
    [InlineData("COALESCE(row_version, thumbnail)")]
    [InlineData("COALESCE(scan, original)")]
    public void A_binary_value_beside_another_kind_or_a_rowversion_beside_a_binary_value_is_not_modelled(string expression) =>
        Assert.StartsWith("unknown", Typed(Procedure($"SELECT {expression} AS v FROM shop.product_image;"), "v"), StringComparison.Ordinal);

    [Fact]
    public void A_union_of_binary_columns_takes_the_longer_varying_type()
    {
        var doc = Procedure("SELECT checksum AS v FROM shop.product_image UNION ALL SELECT thumbnail FROM shop.product_image;");

        Assert.Equal("varbinary(8000) at most 8000", Typed(doc, "v"));
    }

    [Fact]
    public void Isnull_of_a_binary_value_is_the_first_s_type_and_never_null_beside_a_value_never_null()
    {
        // Isnull_cuts_or_pads_a_binary_second_argument_to_the_firsts_type.
        var doc = Procedure("SELECT ISNULL(thumbnail, 0x) AS v, ISNULL(scan, 0x00) AS s FROM shop.product_image;");

        Assert.Equal("varbinary(8000) at most 8000", Typed(doc, "v"));
        Assert.Empty(Field(doc, "v").NullableBecause);
        Assert.Equal("image", Typed(doc, "s"));
    }

    // ---- concatenation -------------------------------------------------------------------------------------------------

    [Theory]
    // Binary_plus_is_as_long_as_both_up_to_8000_bytes_and_fixed_only_when_both_are.
    [InlineData("0x01 + 0x0203", "varbinary(3) at most 3")]
    [InlineData("checksum + checksum", "binary(64) exactly 64")]
    [InlineData("checksum + CAST(0x01 AS varbinary(4))", "varbinary(36) at most 36")]
    [InlineData("thumbnail + checksum", "varbinary(8000) at most 8000")]
    [InlineData("original + 0x01", "varbinary(max)")]
    public void Binary_plus_is_as_long_as_both_and_fixed_only_when_both_are(string expression, string type) =>
        Assert.Equal(type, Typed(Procedure($"SELECT {expression} AS v FROM shop.product_image;"), "v"));

    [Fact]
    public void Binary_plus_is_null_when_either_operand_is()
    {
        var doc = Procedure("SELECT 0x01 + 0x02 AS never, checksum + thumbnail AS maybe FROM shop.product_image;");

        Assert.Empty(Field(doc, "never").NullableBecause);
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "maybe"));
    }

    [Theory]
    // A_binary_value_beside_a_string_or_a_rowversion_under_plus_is_not_modelled: rowversion counts as 40 bytes under +.
    [InlineData("checksum + code")]
    [InlineData("row_version + 0x01")]
    [InlineData("scan + 0x01")]
    public void Binary_plus_beside_a_string_or_of_a_rowversion_or_an_image_is_not_modelled(string expression) =>
        Assert.StartsWith("unknown", Typed(Procedure($"SELECT {expression} AS v FROM shop.product_image;"), "v"), StringComparison.Ordinal);

    [Theory]
    // Concat_reads_a_binary_value_as_varchar_of_its_bytes.
    [InlineData("CONCAT(checksum, 0x0102)", "string varchar(34)")]
    [InlineData("CONCAT(code, checksum)", "string varchar(42)")]
    [InlineData("CONCAT(original, 0x01)", "string varchar(max)")]
    [InlineData("CONCAT_WS(0x2C, checksum, 0x01)", "string varchar(34)")]
    public void Concat_reads_a_binary_value_as_varchar_of_its_bytes(string expression, string type)
    {
        var doc = Procedure($"SELECT {expression} AS v FROM shop.product_image;");

        Assert.Equal(type, Typed(doc, "v"));
        Assert.Empty(Field(doc, "v").NullableBecause);
    }

    [Theory]
    // Beside nvarchar the server counts a binary value as half its bytes rounded up, in characters it reads its bytes as.
    [InlineData("CONCAT(checksum, caption)")]
    [InlineData("CONCAT(row_version, 0x01)")]
    public void Concat_of_a_binary_value_beside_nvarchar_or_of_a_rowversion_is_not_modelled(string expression) =>
        Assert.StartsWith("unknown", Typed(Procedure($"SELECT {expression} AS v FROM shop.product_image;"), "v"), StringComparison.Ordinal);

    // ---- parts -----------------------------------------------------------------------------------------------------------

    [Theory]
    // Substring_of_a_binary_value_is_varbinary_and_left_and_right_varchar_as_long_as_a_literal_length_allows.
    [InlineData("SUBSTRING(checksum, 1, 4)", "varbinary(4) at most 4")]
    [InlineData("SUBSTRING(thumbnail, 2, image_id)", "varbinary(8000) at most 8000")]
    [InlineData("SUBSTRING(original, 1, 2)", "varbinary(max)")]
    [InlineData("SUBSTRING(checksum, 1, 99)", "varbinary(32) at most 32")]
    [InlineData("LEFT(checksum, 4)", "string varchar(4)")]
    [InlineData("RIGHT(thumbnail, 2)", "string varchar(2)")]
    [InlineData("LEFT(original, 3)", "string varchar(max)")]
    public void A_part_of_a_binary_value_is_varbinary_by_substring_and_varchar_by_left_or_right(string expression, string type) =>
        Assert.Equal(type, Typed(Procedure($"SELECT {expression} AS v FROM shop.product_image;"), "v"));

    [Theory]
    [InlineData("SUBSTRING(row_version, 1, 2)")]
    [InlineData("SUBSTRING(scan, 1, 2)")]
    [InlineData("LEFT(row_version, 2)")]
    public void A_part_of_a_rowversion_or_an_image_is_not_modelled(string expression) =>
        Assert.StartsWith("unknown", Typed(Procedure($"SELECT {expression} AS v FROM shop.product_image;"), "v"), StringComparison.Ordinal);

    // ---- lengths and aggregates ----------------------------------------------------------------------------------------

    [Theory]
    // Datalength_of_a_binary_value_is_int_and_bigint_over_max.
    [InlineData("DATALENGTH(checksum)", "number int")]
    [InlineData("DATALENGTH(original)", "number bigint")]
    [InlineData("DATALENGTH(scan)", "number int")]
    [InlineData("DATALENGTH(row_version)", "number int")]
    public void Datalength_of_a_binary_value_is_int_or_bigint_over_max(string expression, string type) =>
        Assert.Equal(type, Typed(Procedure($"SELECT {expression} AS v FROM shop.product_image;"), "v"));

    [Fact]
    public void Max_and_min_keep_a_binary_value_s_type_and_refuse_an_image()
    {
        // Max_and_min_keep_a_binary_or_rowversion_type_and_refuse_an_image.
        var doc = Procedure("SELECT MAX(thumbnail) AS t, MIN(row_version) AS r, MAX(scan) AS s FROM shop.product_image;");

        Assert.Equal("varbinary(8000) at most 8000", Typed(doc, "t"));
        Assert.Equal("timestamp exactly 8", Typed(doc, "r"));
        Assert.StartsWith("unknown", Typed(doc, "s"), StringComparison.Ordinal);
    }

    [Theory]
    // Refused when the procedure is created (8116, over the catalogue sweep in CatalogueSweepGridTests).
    [InlineData("STRING_AGG(checksum, ',')")]
    [InlineData("STRING_AGG(thumbnail, ',')")]
    [InlineData("STRING_AGG(row_version, ',')")]
    public void String_agg_of_a_binary_value_is_refused(string expression) =>
        Assert.StartsWith("unknown", Typed(Procedure($"SELECT {expression} AS v FROM shop.product_image;"), "v"), StringComparison.Ordinal);

    // ---- query files ---------------------------------------------------------------------------------------------------

    [Theory]
    // A_parameter_compared_with_a_binary_value_takes_its_type_under_equality_and_varbinary_8000_under_ordering, and
    // A_parameter_compared_with_a_binary_column_of_any_kind_takes_its_type_under_equality_and_varbinary_under_ordering.
    [InlineData("checksum = @p", "binary(32)")]
    [InlineData("thumbnail = @p", "varbinary(8000)")]
    [InlineData("row_version = @p", "timestamp")]
    [InlineData("checksum < @p", "varbinary(8000)")]
    [InlineData("original > @p", "varbinary(max)")]
    public void A_query_file_s_parameter_compared_with_a_binary_value_is_typed_from_it(string condition, string type)
    {
        var doc = InferQuery.Infer(Tables, $"SELECT image_id FROM shop.product_image WHERE {condition};");

        var parameter = Assert.IsType<IrScalar>(Assert.Single(doc.Query().Parameters).Type);
        Assert.Equal((IrScalarKind.Bytes, type), (parameter.Kind, parameter.Sql));
    }
}
