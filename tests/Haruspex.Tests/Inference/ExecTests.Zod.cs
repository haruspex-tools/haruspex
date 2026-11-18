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

        // The callee's rows and the caller's are one shape, declared once and exported under each one's own name.
        var zod = ZodEmitter.Emit(doc).Expected();
        Assert.Matches("export const PRowSchema = shape[0-9a-f]{6}\n", zod);
        Assert.Matches("export const QRowSchema = shape[0-9a-f]{6}\n", zod);
        Assert.Contains("export const PSchema = z.array(PRowSchema)", zod, StringComparison.Ordinal);
    }
}
