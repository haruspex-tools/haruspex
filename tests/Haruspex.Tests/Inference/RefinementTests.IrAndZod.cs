using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

public partial class RefinementTests
{
    [Fact]
    public void The_ir_writes_each_results_cardinality()
    {
        var json = IrJsonWriter.Write(Procedure("SELECT t.name FROM dbo.t AS t WHERE t.id = @x;", "@x int"));

        Assert.Contains($"\"irVersion\": \"{IrDocument.CurrentVersion}\"", json, StringComparison.Ordinal);
        Assert.Contains("\"cardinality\": \"atMostOne\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void The_zod_bounds_a_rowset_by_its_cardinality()
    {
        var one = ZodEmitter.Emit(Procedure("SELECT COUNT_BIG(*) AS n FROM dbo.t AS t;")).Expected();
        var atMostOne = ZodEmitter.Emit(Procedure("SELECT t.name FROM dbo.t AS t WHERE t.id = @x;", "@x int")).Expected();
        var mayNotRun = ZodEmitter.Emit(Procedure("IF @x = 1 SELECT COUNT_BIG(*) AS n FROM dbo.t AS t;", "@x int")).Expected();
        var json = ZodEmitter.Emit(Procedure("SELECT t.name FROM dbo.t AS t WHERE t.id = @x FOR JSON PATH;", "@x int")).Expected();

        Assert.Contains("export const PSchema = z.array(PRowSchema).length(1)\n", one, StringComparison.Ordinal);
        Assert.Contains("export const PSchema = z.array(PRowSchema).max(1)\n", atMostOne, StringComparison.Ordinal);
        Assert.Contains("export const PSchema = z.array(PRowSchema).max(1)\n", mayNotRun, StringComparison.Ordinal);
        // FOR JSON over no rows returns no row, which reads as null, so an array that arrives holds the one row.
        Assert.Contains("export const PSchema = z.array(PRowSchema).length(1).nullable()\n", json, StringComparison.Ordinal);
    }
}
