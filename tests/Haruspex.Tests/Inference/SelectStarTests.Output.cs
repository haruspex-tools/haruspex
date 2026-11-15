using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

// inserted.* and deleted.* in a write's OUTPUT: stars over the table written, which leave out what a star over it leaves out
// (Output_stars_leave_out_what_a_star_over_the_target_leaves_out).
public partial class SelectStarTests
{
    private const string Price = "CREATE TABLE shop.price (sku varchar(12) NOT NULL PRIMARY KEY, amount decimal(9,2) NULL, valid_from datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL, valid_to datetime2 GENERATED ALWAYS AS ROW END HIDDEN NOT NULL, PERIOD FOR SYSTEM_TIME (valid_from, valid_to));";

    private const string Parcel = "CREATE TABLE shop.parcel (parcel_id int NOT NULL, weight int SPARSE NULL, label nvarchar(20) NULL, extras xml COLUMN_SET FOR ALL_SPARSE_COLUMNS);";

    [Theory]
    [InlineData("INSERT INTO shop.price (sku, amount) OUTPUT inserted.* VALUES ('RIB', 1.20);")]
    [InlineData("DELETE FROM shop.price OUTPUT deleted.* WHERE sku = 'RIB';")]
    [InlineData("UPDATE shop.price SET amount = 2.00 OUTPUT inserted.* WHERE sku = 'RIB';")]
    public void An_output_star_leaves_out_hidden_period_columns(string write)
    {
        var doc = Procedure(write, others: Price);

        AllTyped(doc);
        Assert.Equal(["sku", "amount"], Names(doc));
    }

    [Fact]
    public void An_output_star_over_a_column_set_gives_the_set_in_place_of_its_sparse_columns()
    {
        var doc = Procedure("UPDATE shop.parcel SET label = N'x' OUTPUT inserted.*, deleted.parcel_id AS was_id WHERE parcel_id = 1;", others: Parcel);

        Assert.Equal(["parcel_id", "label", "extras", "was_id"], Names(doc));
    }

    [Fact]
    public void A_hidden_column_named_in_an_output_is_still_read()
    {
        // SQL Server allows a hidden column by name; only the star leaves it out.
        var doc = Procedure("UPDATE shop.price SET amount = 2.00 OUTPUT inserted.sku, inserted.valid_from WHERE sku = 'RIB';", others: Price);

        AllTyped(doc);
        Assert.Equal(["sku", "valid_from"], Names(doc));
    }

    [Fact]
    public void An_output_star_over_a_table_variable_follows_its_declaration()
    {
        var doc = Procedure("""
            DECLARE @parcels TABLE (parcel_id int NOT NULL, weight int SPARSE NULL, extras xml COLUMN_SET FOR ALL_SPARSE_COLUMNS);
            INSERT INTO @parcels (parcel_id) OUTPUT inserted.* VALUES (1);
            """);

        Assert.Equal(["parcel_id", "extras"], Names(doc));
    }
}
