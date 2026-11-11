using Haruspex.Emit.Zod;

namespace Haruspex.Tests.Inference;

public partial class ExecTests
{
    [Fact]
    public void A_callers_zod_holds_the_callees_results()
    {
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p AS EXEC dbo.q;"));

        var zod = ZodEmitter.Emit(doc);
        Assert.Contains("export const PRowSchema = z.object({", zod, StringComparison.Ordinal);
        Assert.Contains("export const PSchema = z.array(PRowSchema)", zod, StringComparison.Ordinal);
    }
}
