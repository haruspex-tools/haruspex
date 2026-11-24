using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// Values computed from a masked column: the server masks the result of an expression whose value comes from one as a whole,
/// through views, derived tables, set operations and scalar functions, but not a value a condition on one chooses.
/// </summary>
public partial class MaskingTests
{
    [Fact]
    public void An_expression_computed_from_a_masked_column_claims_only_what_its_type_does()
    {
        var doc = Procedure("""
            SELECT ISNULL(band, 1) AS kept, ISNULL(band_plain, 1) AS kept_plain, CAST(code AS char(6)) AS cast, CAST(code_plain AS char(6)) AS cast_plain,
                   rating + 1 AS plus, '[' + code + ']' AS wrapped, COUNT(band) OVER () AS counted, LEN(note) AS length
            FROM clinic.patient;
            """);

        Assert.NotNull(Facets(doc, "kept_plain")!.Values);
        Assert.Null(Facets(doc, "kept")!.Values);
        Assert.True(Facets(doc, "cast_plain")!.Length!.IsFixed);
        Assert.False(Facets(doc, "cast")!.Length!.IsFixed);
        foreach (var name in new[] { "kept", "cast", "plus", "wrapped", "counted", "length" })
            Assert.Equal(TypeOnly(Facets(doc, name)), Facets(doc, name));
    }

    [Fact]
    public void A_condition_on_a_masked_column_leaves_the_value_it_chooses_unmasked()
    {
        var doc = Procedure("SELECT CASE WHEN rating = 4 THEN 'a' ELSE 'b' END AS chosen, IIF(band = 2, 'y', 'n') AS picked FROM clinic.patient WHERE note IS NOT NULL;");

        Assert.Equal(["a", "b"], Facets(doc, "chosen")!.Values!.Items.Select(v => v.Text));
        Assert.Equal(["n", "y"], Facets(doc, "picked")!.Values!.Items.Select(v => v.Text).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_case_that_may_give_a_masked_value_is_masked_whichever_branch_gives_it()
    {
        var doc = Procedure("""
            SELECT CASE WHEN id = 1 THEN CAST(code AS char(6)) ELSE CAST('ABCDEF' AS char(6)) END AS chosen,
                   CASE WHEN id = 1 THEN CAST(code_plain AS char(6)) ELSE CAST('ABCDEF' AS char(6)) END AS chosen_plain
            FROM clinic.patient;
            """);

        Assert.True(Facets(doc, "chosen_plain")!.Length!.IsFixed);
        Assert.False(Facets(doc, "chosen")!.Length!.IsFixed);
    }

    [Fact]
    public void A_set_operation_s_column_is_masked_when_one_query_s_is()
    {
        var masked = Procedure("SELECT band AS b FROM clinic.patient UNION ALL SELECT 9;");
        var plain = Procedure("SELECT band_plain AS b FROM clinic.patient UNION ALL SELECT 9;");

        Assert.NotNull(Facets(plain, "b")!.Values);
        Assert.Null(Facets(masked, "b")!.Values);
        Assert.Equal(TypeOnly(Facets(masked, "b")), Facets(masked, "b"));
    }

    [Fact]
    public void A_view_and_a_derived_table_pass_their_column_s_mask_on()
    {
        var doc = Procedure(
            "SELECT v.band, d.code FROM clinic.banded AS v JOIN (SELECT id, code FROM clinic.patient) AS d ON d.id = v.id;",
            more: ("view.sql", "CREATE VIEW clinic.banded AS SELECT id, band FROM clinic.patient;"));

        Assert.Null(Facets(doc, "band")!.Values);
        Assert.False(Facets(doc, "code")!.Length!.IsFixed);
    }

    [Fact]
    public void A_value_kept_in_a_variable_stays_masked_which_claims_less_than_the_server()
    {
        var doc = Procedure("DECLARE @band int = (SELECT band FROM clinic.patient WHERE id = 1); SELECT @band AS band, @band * 0 + 5 AS five;");

        Assert.Null(Facets(doc, "band")?.Values);
        Assert.Equal(TypeOnly(Facets(doc, "five")), Facets(doc, "five"));
    }

    [Fact]
    public void A_scalar_function_that_reads_a_masked_column_returns_its_mask()
    {
        var doc = Procedure(
            "SELECT clinic.code_of(id) AS masked, clinic.constant() AS plain FROM clinic.patient;",
            more:
            [
                ("code_of.sql", "CREATE FUNCTION clinic.code_of (@id int) RETURNS char(6) AS BEGIN RETURN (SELECT code FROM clinic.patient WHERE id = @id); END;"),
                ("constant.sql", "CREATE FUNCTION clinic.constant () RETURNS char(6) AS BEGIN RETURN 'ABCDEF'; END;"),
            ]);

        Assert.True(Facets(doc, "plain")!.Length!.IsFixed);
        Assert.False(Facets(doc, "masked")!.Length!.IsFixed);
    }

    [Fact]
    public void Json_computed_from_a_masked_column_is_not_json()
    {
        var doc = Procedure("SELECT JSON_OBJECT('band': band) AS made, JSON_OBJECT('band': band_plain) AS made_plain FROM clinic.patient;");

        Assert.IsType<IrUnknownType>(Field(doc, "made").Type);
        Assert.IsNotType<IrUnknownType>(Field(doc, "made_plain").Type);
    }

    [Fact]
    public void A_for_json_that_nests_json_computed_from_a_masked_column_has_results_that_cannot_be_listed()
    {
        var doc = Procedure("SELECT o.id, (SELECT n.band FROM clinic.patient AS n WHERE n.id = o.id FOR JSON PATH) AS kids FROM clinic.patient AS o FOR JSON PATH;");

        Assert.Null(Object(doc, "clinic.p").Results);
        Assert.Equal(IrUnknownReason.JsonShapeUnknown, Unknown(doc, "results").Reason);
    }

    [Fact]
    public void A_for_json_masks_a_masked_column_s_value_and_keeps_its_own_shape()
    {
        var doc = Procedure("SELECT id, band, band_plain FROM clinic.patient FOR JSON PATH;");

        var row = Fields(doc);
        Assert.Null(((IrScalar)Assert.Single(row, f => f.Name == "band").Type).Facets?.Values);
        Assert.NotNull(((IrScalar)Assert.Single(row, f => f.Name == "band_plain").Type).Facets?.Values);
    }
}
