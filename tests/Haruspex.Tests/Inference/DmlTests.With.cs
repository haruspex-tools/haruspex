using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

public partial class DmlTests
{
    [Fact]
    public void A_cte_target_over_a_temporary_table_leaves_it_unknown()
    {
        // The write goes through to the table underneath.
        var doc = Doc(("p", """
            CREATE PROCEDURE dbo.p AS
            DECLARE @one int = 1;
            SELECT @one AS n INTO #t;
            WITH c AS (SELECT t.n FROM #t AS t) UPDATE c SET n = NULL;
            SELECT t.n FROM #t AS t;
            """));

        Assert.IsType<IrUnknownType>(Field(doc, "n").Type);
    }
}
