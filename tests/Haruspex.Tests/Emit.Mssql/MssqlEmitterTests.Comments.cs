using Haruspex.Emit.Mssql;
using Haruspex.Ir;

namespace Haruspex.Tests.Emit.Mssql;

/// <summary>A function's comment says why a result set may be missing (<c>"comments"</c>), and nothing more under none.</summary>
public partial class MssqlEmitterTests
{
    [Fact]
    public void A_function_s_comment_says_why_its_result_may_be_missing_and_the_command_that_explains_it()
    {
        var obj = Procedure("get_order", [], new IrResult(IrEncoding.Json, Int, [new IrNullReason(IrNullRule.NoRows, "p.sql:2:1")]));
        var document = Document(obj);

        var ts = MssqlEmitter.Emit(document, new MssqlOptions()).Expected();
        var none = MssqlEmitter.Emit(document, new MssqlOptions { Comments = IrComments.None }).Expected();

        Assert.Contains("/**\n * Calls shop.get_order.\n * Its result may be missing: the query returns nothing when no rows match (no-rows).\n * Why: haruspex explain shop.get_order\n */\nexport async function", ts, StringComparison.Ordinal);
        Assert.Contains("/** Calls shop.get_order. */\nexport async function", none, StringComparison.Ordinal);
    }
}
