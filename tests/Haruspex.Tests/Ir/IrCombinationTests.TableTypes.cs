using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// A table-valued parameter combined: its columns narrowed as any parameter is, so the rows the combined function sends are rows
/// both procedures' types take; a change that one table can't be sent to both through (another type, other columns) is reported
/// and its procedure left out.
/// </summary>
public partial class IrCombinationTests
{
    private const string Lines = "CREATE TYPE shop.lines AS TABLE (id int NOT NULL, qty smallint NOT NULL, note nvarchar(20) NULL, PRIMARY KEY (id));";

    private const string AddLines = "CREATE PROCEDURE shop.add_lines @lines shop.lines READONLY AS SELECT 1 AS one;";

    private static IrDocument Taking(string type) =>
        InferProcedure.Infer("shop.*", ("tables.sql", Orders), ("type.sql", type), ("procedures.sql", AddLines));

    private static IrObjectType Row(IrDocument document) =>
        document.Types[((IrRef)((IrArray)Parameter(document, "shop.add_lines", "@lines").Type).Items).Name];

    [Fact]
    public void A_table_type_s_columns_narrow_to_what_both_types_take_and_every_key_and_check_of_either_holds()
    {
        var baseline = Taking(Edit(Lines, "qty smallint NOT NULL", "qty smallint NULL CHECK (qty > 0)"));
        var candidate = Taking(Edit(Edit(Lines, "nvarchar(20)", "nvarchar(40)"), "PRIMARY KEY (id)", "PRIMARY KEY (id), UNIQUE (note)"));

        var combined = Combine(baseline, candidate);
        var row = Row(combined.Document);
        var definition = combined.Document.TableTypes["shop.lines"];

        Assert.Empty(combined.Findings);
        Assert.Equal([false, false, true], row.Fields.Select(f => f.Nullable)); // qty NOT NULL in the candidate
        Assert.Equal(20, ((IrScalar)row.Fields[2].Type).Facets!.Length!.Value); // the baseline's shorter note
        Assert.Equal(["qty > 0"], definition.Checks);
        Assert.Equal([["id"], ["note"]], definition.Keys.Select(k => k.Columns.ToArray()));
        Assert.Equal(IrDocument.TableTypesVersion, combined.Document.Version);
        FitsBoth(combined, baseline, candidate);
    }

    [Theory]
    [InlineData("a column added", "note nvarchar(20) NULL,", "note nvarchar(20) NULL, extra int NULL,")]
    [InlineData("two columns swapped", "id int NOT NULL, qty smallint NOT NULL", "qty smallint NOT NULL, id int NOT NULL")]
    public void A_table_type_whose_columns_changed_can_t_be_bridged(string change, string from, string to)
    {
        var combined = Combine(Taking(Lines), Taking(Edit(Lines, from, to)));

        var finding = Assert.Single(combined.Findings);
        Assert.True(finding.Kind == IrCombinationKind.Unbridgeable, change);
        Assert.Equal(("shop.add_lines", "parameters[\"@lines\"]"), (finding.Object, finding.Path));
        Assert.Contains("matches by place", finding.What, StringComparison.Ordinal);
        Assert.Empty(combined.Document.Objects);
        Assert.Empty(combined.Document.TableTypes);
    }

    [Fact]
    public void An_unchanged_table_type_combines_as_it_is()
    {
        var document = Taking(Lines);

        var combined = Combine(document, document);

        Assert.Empty(combined.Findings);
        Assert.Equal(document.TableTypes, combined.Document.TableTypes);
        Assert.Equal(Row(document), Row(combined.Document));
    }
}
