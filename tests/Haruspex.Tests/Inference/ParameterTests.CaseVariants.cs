using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

public partial class ParameterTests
{
    [Fact]
    public void A_check_on_one_of_two_columns_whose_names_differ_only_in_case_narrows_neither_row_field()
    {
        // A database of a case-sensitive collation takes this type; a CHECK names qty, which ignoring case is Qty too. Read so,
        // Qty's field would refuse 100, which the server takes (decision 0107).
        var document = WithTypes(
            "CREATE PROCEDURE shop.p @lines shop.lines READONLY AS SELECT 1 AS one;",
            "CREATE TYPE shop.lines AS TABLE (id int NOT NULL CHECK (id > 0), qty int NULL CHECK (qty BETWEEN 1 AND 5), Qty int NOT NULL);");
        var row = RowOf(document, Assert.Single(document.Objects[0].Parameters));

        Assert.Equal(["id", "qty", "Qty"], row.Fields.Select(f => f.Name));
        static IrFacets Facets(IrField field) => Assert.IsType<IrScalar>(field.Type).Facets!;
        Assert.Equal(("0", IrFactSourceKind.Check), (Facets(row.Fields[0]).Min!.Value, Facets(row.Fields[0]).Min!.From.Kind));

        // Only the type's own range: no field refuses 100.
        Assert.All(row.Fields.Skip(1), f => Assert.Equal(
            ("-2147483648", "2147483647", IrFactSourceKind.Type),
            (Facets(f).Min!.Value, Facets(f).Max!.Value, Facets(f).Max!.From.Kind)));
    }
}
