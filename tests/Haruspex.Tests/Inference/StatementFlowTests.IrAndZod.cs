using Haruspex.Emit.Zod;

namespace Haruspex.Tests.Inference;

public partial class StatementFlowTests
{
    [Fact]
    public void A_single_object_result_that_may_not_run_is_nullable_in_zod()
    {
        var ts = ZodEmitter.Emit(Procedure("IF @x IS NULL RETURN;\nSELECT t.id FROM dbo.t AS t FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;", "@x int")).Expected();

        Assert.Contains("export const PSchema = PRowSchema.nullable()\n", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void A_union_puts_a_variant_with_more_keys_first_and_each_rejects_the_others_keys()
    {
        // z.union takes the first variant that accepts a row, and z.object strips keys it does not know.
        var doc = Procedure("IF @x = 1 SELECT u.id FROM dbo.u AS u;\nELSE SELECT t.id, t.code FROM dbo.t AS t;", "@x int");

        Assert.Contains("export const PSchema = z.union([z.array(PRow2Schema), z.array(PRowSchema.extend({ code: z.never().optional() }))])\n", ZodEmitter.Emit(doc).Expected(), StringComparison.Ordinal);
    }

    [Fact]
    public void Each_json_variant_rejects_the_keys_only_the_others_have_so_none_drops_them()
    {
        // A key left out when null is optional, so either variant would accept the other's objects without this.
        var doc = Procedure("IF @x = 1 SELECT t.id, t.name FROM dbo.t AS t FOR JSON PATH;\nELSE SELECT u.id, u.t_id FROM dbo.u AS u FOR JSON PATH;", "@x int");

        Assert.Contains(
            "z.union([z.array(PRowSchema.extend({ t_id: z.never().optional() })), z.array(PRow2Schema.extend({ name: z.never().optional() }))]).nullable()",
            ZodEmitter.Emit(doc).Expected(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_rowset_that_may_not_run_reads_as_no_rows()
    {
        var doc = Procedure("IF @x IS NULL RETURN;\nSELECT t.id FROM dbo.t AS t;", "@x int");

        Assert.Contains("export const PSchema = z.array(PRowSchema)\n", ZodEmitter.Emit(doc).Expected(), StringComparison.Ordinal);
        Assert.Contains("[] when it does not run", ZodEmitter.Emit(doc).Expected(), StringComparison.Ordinal);
    }
}
