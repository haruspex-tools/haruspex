using Haruspex.SqlServer.Model;

namespace Haruspex.Tests.Model;

public class CollationCatalogueTests
{
    [Fact]
    public void The_list_has_every_collation_of_sql_server_2025_once()
    {
        // The_collations_haruspex_lists_are_exactly_the_servers_with_their_code_pages checks them against the server, name by name.
        Assert.Equal(5540, CollationCatalogue.Names.Count());
        Assert.Equal(5540, CollationCatalogue.Names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("SQL_Latin1_General_CP1_CI_AS", 1252)]
    [InlineData("sql_latin1_general_cp1_ci_as", 1252)]
    [InlineData("Latin1_General_100_CI_AS_SC_UTF8", CollationCatalogue.Utf8)]
    [InlineData("Latin1_General_100_BIN2_UTF8", CollationCatalogue.Utf8)]
    [InlineData("Japanese_XJIS_140_CI_AS_VSS", 932)]
    [InlineData("Chinese_Simplified_Stroke_Order_160_CS_AS_KS_WS_SC_UTF8", CollationCatalogue.Utf8)]
    [InlineData("Indic_General_90_CI_AS", CollationCatalogue.UnicodeOnly)]
    [InlineData("SQL_Latin1_General_CP437_BIN", 437)]
    public void A_collation_has_the_code_page_its_char_data_is_stored_in(string name, int codePage) =>
        Assert.Equal(codePage, CollationCatalogue.CodePage(name));

    [Theory]
    [InlineData("Latin1_General_100_CI_AS_SC_UFT8")]
    [InlineData("Japanes_XJIS_140_CI_AS")]
    [InlineData("Latin1_General_CI_AZ")]
    [InlineData("Nonsense")]
    // Real options in an order or a version SQL Server doesn't have.
    [InlineData("Latin1_General_100_AS_CI")]
    [InlineData("Latin1_General_140_CI_AS")]
    [InlineData("Latin1_General_CI_AS_SC")]
    public void A_name_no_collation_has_is_unknown(string name)
    {
        Assert.False(CollationCatalogue.IsKnown(name));
        Assert.Null(CollationCatalogue.CodePage(name));
    }
}
