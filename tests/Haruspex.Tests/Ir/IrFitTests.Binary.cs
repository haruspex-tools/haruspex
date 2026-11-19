namespace Haruspex.Tests.Ir;

/// <summary>
/// A binary value under decision 0068: a result fits when what the database returns is no longer, nor of another fixed length,
/// than what the application takes; a parameter when what the application sends is no longer than what the procedure takes.
/// <c>BaselineFitTests</c> calls each through the old function.
/// </summary>
public partial class IrFitTests
{
    private const string Images = """
        CREATE TABLE shop.image (image_id int NOT NULL PRIMARY KEY, thumbnail varbinary(100) NOT NULL, checksum binary(32) NOT NULL);
        """;

    private const string GetImage = "CREATE PROCEDURE shop.get_image AS SELECT i.thumbnail, i.checksum FROM shop.image AS i;";

    private const string AddImage = "CREATE PROCEDURE shop.add_image @thumbnail varbinary(100) AS SELECT 1 AS done;";

    [Fact]
    public void A_binary_result_that_became_shorter_fits_and_one_that_became_longer_or_lost_its_fixed_length_is_refused()
    {
        var before = Infer(GetImage, Images);

        Assert.Empty(Fit(before, Infer(GetImage, Edit(Images, "thumbnail varbinary(100)", "thumbnail varbinary(50)"))));
        Assert.Equal(
            ["refused: shop.get_image results[0][].thumbnail: length changed from at most 100 bytes to at most 200 bytes"],
            Fit(before, Infer(GetImage, Edit(Images, "thumbnail varbinary(100)", "thumbnail varbinary(200)"))));
        Assert.Equal(
            ["refused: shop.get_image results[0][].checksum: length changed from exactly 32 bytes to at most 32 bytes"],
            Fit(before, Infer(GetImage, Edit(Images, "checksum binary(32)", "checksum varbinary(32)"))));
    }

    [Fact]
    public void A_binary_result_that_became_text_is_refused()
    {
        var before = Infer(GetImage, Images);

        Assert.Equal(
            ["refused: shop.get_image results[0][].thumbnail: type changed from bytes to string"],
            Fit(before, Infer(GetImage, Edit(Images, "thumbnail varbinary(100)", "thumbnail varchar(100)"))));
    }

    [Fact]
    public void A_binary_parameter_that_became_longer_fits_and_one_that_became_shorter_is_refused()
    {
        var before = Infer(AddImage, Images);

        Assert.Empty(Fit(before, Infer(Edit(AddImage, "varbinary(100)", "varbinary(max)"), Images)));
        Assert.Equal(
            ["refused: shop.add_image parameters[\"@thumbnail\"]: length changed from at most 100 bytes to at most 50 bytes"],
            Fit(before, Infer(Edit(AddImage, "varbinary(100)", "varbinary(50)"), Images)));
    }
}
