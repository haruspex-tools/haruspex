using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests.Config;

public partial class ConfigApplicationTests
{
    [Fact]
    public void Nullable_false_makes_the_value_non_null_and_is_listed_with_its_reason()
    {
        var doc = Apply(GetCustomer, """{ "procedures": { "sales.get_customer": { "paths": { "[].name": { "nullable": false, "why": "every customer has a name" } } } } }""").Document;

        Assert.False(Field(doc, "GetCustomerRow", "name").Nullable);
        Assert.Equal(new IrOverride("sales.get_customer", "results[0][].name", IrOverrideSetting.NotNull, "every customer has a name"), Assert.Single(doc.Overrides));

        // Never null, so never left out: the key is required.
        Assert.Contains("  name: z.string().max(40),\n", ZodEmitter.Emit(doc), StringComparison.Ordinal);
        Assert.Contains("\"setting\": \"not-null\"", IrJsonWriter.Write(doc), StringComparison.Ordinal);
    }

    [Fact]
    public void Nullable_false_on_the_whole_result_says_it_is_never_absent()
    {
        var doc = Apply("CREATE PROCEDURE dbo.p AS SELECT TOP (1) c.customer_id AS id FROM sales.customer AS c FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;\nGO\n",
            """{ "procedures": { "dbo.p": { "paths": { "": { "nullable": false, "why": "there is always a customer" } } } } }""").Document;

        Assert.False(doc.Objects[0].Results![0].MayBeAbsent);
    }

    [Fact]
    public void Nullable_false_the_inference_agrees_with_is_a_warning()
    {
        var run = Apply(GetCustomer, """{ "procedures": { "sales.get_customer": { "paths": { "[].id": { "nullable": false, "why": "a key" } } } } }""");

        Assert.Equal(
            ["haruspex.json:1:54: warning HAR4110: procedures[\"sales.get_customer\"].paths[\"[].id\"]: \"nullable\": false agrees with the inference, which already proves the value at results[0][].id is never null. Remove it."],
            run.Problems);
        Assert.Empty(run.Document.Overrides);
    }

    [Fact]
    public void Nullable_false_on_what_json_text_holds_is_an_error()
    {
        var run = Apply("CREATE PROCEDURE dbo.p AS SELECT (SELECT c.customer_id AS id FROM sales.customer AS c FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS one;\nGO\n",
            """{ "procedures": { "dbo.p": { "paths": { "[].one<json>": { "nullable": false, "why": "x" } } } } }""");

        Assert.Equal("haruspex.json:1:41: error HAR4107: procedures[\"dbo.p\"].paths[\"[].one<json>\"]: the value at results[0][].one<json> is what JSON text holds, which is never null itself; nothing can be declared about its nullability.", run.Error);
    }
}
