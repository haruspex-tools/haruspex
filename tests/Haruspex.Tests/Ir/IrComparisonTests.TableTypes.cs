using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>A table type's definition changed under its name is a change of its parameter, as the two documents carry it.</summary>
public partial class IrComparisonTests
{
    [Fact]
    public void A_table_type_whose_definition_changed_is_a_change_of_each_parameter_of_it()
    {
        const string Procedure = "CREATE PROCEDURE shop.add_lines @lines shop.lines READONLY AS SELECT 1 AS one;";
        static IrDocument Taking(string check) => InferProcedure.Infer(
            "shop.*", ("type.sql", $"CREATE TYPE shop.lines AS TABLE (qty smallint NOT NULL{check});"), ("procedures.sql", Procedure));

        Assert.Empty(Changes(Taking(""), Taking("")));
        Assert.Equal(
            ["shop.add_lines parameters[\"@lines\"]: table type changed from [shop].[lines] (qty smallint not null) to [shop].[lines] (qty smallint not null; check (qty > 0))"],
            Changes(Taking(""), Taking(" CHECK (qty > 0)")).Where(c => c.Contains("table type", StringComparison.Ordinal)));
    }
}
