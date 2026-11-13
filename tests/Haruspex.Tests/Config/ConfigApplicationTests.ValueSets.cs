using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests.Config;

public partial class ConfigApplicationTests
{
    [Fact]
    public void Values_exact_makes_a_collation_set_an_enum()
    {
        var before = ZodEmitter.Emit(Apply(GetCustomer, "{}").Document).Expected();
        Assert.Contains("One of \"open\", \"closed\", as the database compares strings.", before, StringComparison.Ordinal);
        Assert.Contains("  status: z.string().max(8),\n", before, StringComparison.Ordinal);

        var doc = Apply(GetCustomer, """{ "procedures": { "sales.get_customer": { "paths": { "[].status": { "values": "exact", "why": "written in lower case only" } } } } }""").Document;

        Assert.Contains("  status: z.enum([\"open\", \"closed\"]),\n", ZodEmitter.Emit(doc).Expected(), StringComparison.Ordinal);
        Assert.Equal(new IrOverride("sales.get_customer", "results[0][].status", IrOverrideSetting.ExactValues, "written in lower case only"), Assert.Single(doc.Overrides));
    }

    [Fact]
    public void Values_exact_on_a_set_already_exact_is_a_warning()
    {
        var run = Apply(GetCustomer, """{ "procedures": { "sales.get_customer": { "paths": { "[].level": { "values": "exact", "why": "x" } } } } }""");

        Assert.Equal(
            ["haruspex.json:1:54: warning HAR4111: procedures[\"sales.get_customer\"].paths[\"[].level\"]: \"values\": \"exact\" agrees with the inference, which already proves the set at results[0][].level exact. Remove it."],
            run.Problems);
    }

    [Fact]
    public void Values_exact_where_the_ir_has_no_set_is_an_error_and_fakes_none()
    {
        var run = Apply(GetCustomer, """{ "procedures": { "sales.get_customer": { "paths": { "[].name": { "values": "exact", "why": "x" } } } } }""");

        Assert.Equal(
            "haruspex.json:1:54: error HAR4108: procedures[\"sales.get_customer\"].paths[\"[].name\"]: the value at results[0][].name has no value set to declare exact. A value set is inferred from a CHECK constraint on a column the value passes through unchanged; canonical value sets for other cases are planned, not done.",
            run.Error);
    }

    [Fact]
    public void A_document_with_no_overrides_writes_an_empty_list() =>
        Assert.Contains("\"overrides\": []", IrJsonWriter.Write(Apply(GetCustomer, "{}").Document), StringComparison.Ordinal);
}
