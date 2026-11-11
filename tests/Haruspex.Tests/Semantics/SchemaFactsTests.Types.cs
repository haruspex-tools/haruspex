using Haruspex.SqlServer.Semantics;

namespace Haruspex.Tests.Semantics;

public partial class SchemaFactsTests
{
    [Theory]
    [InlineData("tinyint NULL", "0", "255")]
    [InlineData("smallint NULL", "-32768", "32767")]
    [InlineData("int NULL", "-2147483648", "2147483647")]
    [InlineData("bigint NULL", "-9223372036854775808", "9223372036854775807")]
    [InlineData("decimal(19, 2) NULL", "-99999999999999999.99", "99999999999999999.99")]
    [InlineData("numeric(5, 5) NULL", "-0.99999", "0.99999")]
    [InlineData("decimal NULL", "-999999999999999999", "999999999999999999")]
    [InlineData("decimal(38) NULL", "-99999999999999999999999999999999999999", "99999999999999999999999999999999999999")]
    [InlineData("money NULL", "-922337203685477.5808", "922337203685477.5807")]
    [InlineData("smallmoney NULL", "-214748.3648", "214748.3647")]
    public void An_exact_numeric_type_bounds_its_values(string definition, string min, string max)
    {
        var facts = Column(definition);

        Assert.Equal(min, Bound(facts.Min));
        Assert.Equal(max, Bound(facts.Max));
        Assert.StartsWith("type ", Source(facts.Min?.From), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("decimal(19, 2) NULL", 19, 2)]
    [InlineData("decimal NULL", 18, 0)]
    [InlineData("numeric(9) NULL", 9, 0)]
    [InlineData("money NULL", 19, 4)]
    [InlineData("smallmoney NULL", 10, 4)]
    public void A_decimal_type_has_its_digits(string definition, int precision, int scale)
    {
        var digits = Column(definition).Digits!;

        Assert.Equal((precision, scale), (digits.Precision, digits.Scale));
    }

    [Theory]
    [InlineData("float NULL")]
    [InlineData("real NULL")]
    [InlineData("bit NULL")]
    [InlineData("date NULL")]
    [InlineData("nvarchar(max) NULL")]
    [InlineData("varchar(max) NULL")]
    public void Other_types_claim_no_facets(string definition)
    {
        var facts = Column(definition);

        Assert.NotNull(facts.Type);
        Assert.Equal(new ColumnFacts(facts.Type), facts);
    }

    [Theory]
    [InlineData("nvarchar(40) NULL", 40, false, TextUnit.Utf16, TextEncoding.Unknown)]
    [InlineData("nchar(5) NULL", 5, true, TextUnit.Utf16, TextEncoding.Unknown)]
    [InlineData("nchar NULL", 1, true, TextUnit.Utf16, TextEncoding.Unknown)]
    [InlineData("varchar(5) NULL", 5, false, TextUnit.Bytes, TextEncoding.Unknown)]
    [InlineData("char(5) NULL", 5, true, TextUnit.Bytes, TextEncoding.Unknown)]
    [InlineData("char(5) COLLATE Latin1_General_100_CI_AS NULL", 5, true, TextUnit.Bytes, TextEncoding.SingleByte)]
    [InlineData("varchar(5) COLLATE SQL_Latin1_General_CP1_CI_AS NULL", 5, false, TextUnit.Bytes, TextEncoding.SingleByte)]
    [InlineData("varchar(5) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NULL", 5, false, TextUnit.Bytes, TextEncoding.Utf8)]
    [InlineData("varchar(5) COLLATE Japanese_CI_AS NULL", 5, false, TextUnit.Bytes, TextEncoding.DoubleByte)]
    [InlineData("varchar(5) COLLATE Chinese_PRC_CI_AS NULL", 5, false, TextUnit.Bytes, TextEncoding.DoubleByte)]
    [InlineData("varchar(5) COLLATE Korean_Wansung_CI_AS NULL", 5, false, TextUnit.Bytes, TextEncoding.DoubleByte)]
    // A Unicode type counts UTF-16 code units whatever its collation.
    [InlineData("nvarchar(5) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NULL", 5, false, TextUnit.Utf16, TextEncoding.Unknown)]
    public void A_string_type_has_its_length_and_what_it_counts(string definition, int length, bool isFixed, TextUnit unit, TextEncoding encoding)
    {
        var fact = Column(definition).Length!;

        Assert.Equal((length, isFixed, unit, encoding), (fact.Value, fact.IsFixed, fact.Unit, fact.Encoding));
    }

    [Fact]
    public void A_computed_column_claims_nothing() =>
        Assert.Null(Facts("CREATE TABLE dbo.t (a int NOT NULL, c AS a * 2);").Type);
}
