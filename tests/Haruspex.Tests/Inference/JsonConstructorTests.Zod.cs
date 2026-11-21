using Haruspex.Diagnostics;
using Haruspex.Emit.Zod;

namespace Haruspex.Tests.Inference;

public partial class JsonConstructorTests
{
    [Fact]
    public void The_zod_states_tuples_records_and_array_lengths()
    {
        var ts = ZodEmitter.Emit(Procedure("""
            SELECT json_array(t.id, t.label) AS pair,
                   json_array(t.id, t.name) AS mixed,
                   json_array(t.id, t.name NULL ON NULL) AS slots,
                   (SELECT json_objectagg(u.id: u.qty) FROM dbo.u AS u WHERE u.t_id = t.id) AS qtys
            FROM dbo.t AS t FOR JSON PATH;
            """)).Expected();

        Assert.Contains("  pair: z.tuple([z.int32(), z.string().max(20)]),\n", ts, StringComparison.Ordinal);
        Assert.Contains("  mixed: z.array(z.union([z.int32(), z.string().max(10)])).min(1).max(2),\n", ts, StringComparison.Ordinal);
        Assert.Contains("  slots: z.tuple([z.int32(), z.string().max(10).nullable()]),\n", ts, StringComparison.Ordinal);
        Assert.Contains("  qtys: z.record(z.string(), z.int().min(-32768).max(32767).nullable()).optional(),\n", ts, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SELECT t.id AS [__proto__] FROM dbo.t AS t FOR JSON PATH;")]
    [InlineData("SELECT t.id, json_object('__proto__': t.code) AS o FROM dbo.t AS t FOR JSON PATH;")]
    public void A_key_named_proto_is_typed_in_the_ir_and_refused_by_the_zod_emitter(string body)
    {
        // SQL Server writes the key, and JSON.parse makes it an own property (MssqlDriverObservationTests), so the IR claims
        // it; but Zod skips a declared key of this name, checking nothing and dropping it, so the module is refused.
        var doc = Procedure(body);

        Assert.Contains(doc.Types.Values.SelectMany(t => t.Fields), f => f.Name == "__proto__");
        var refused = Assert.Single(Assert.IsType<Failed<string>>(ZodEmitter.Emit(doc)).Diagnostics);
        Assert.Equal(ZodCodes.KeyUnusable, refused.Code);
    }
}
