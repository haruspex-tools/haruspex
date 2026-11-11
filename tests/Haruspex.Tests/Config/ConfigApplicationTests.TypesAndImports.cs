using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests.Config;

public partial class ConfigApplicationTests
{
    [Fact]
    public void A_type_override_imports_a_schema_and_clears_the_unknowns_under_it()
    {
        const string Body = "CREATE PROCEDURE dbo.p AS SELECT reverse(c.name) AS shout, c.customer_id AS id FROM sales.customer AS c FOR JSON PATH;\nGO\n";
        Assert.Contains(Apply(Body, "{}").Document.Unknowns, u => u.Path == "results[0][].shout");

        var doc = Apply(Body, """{ "procedures": { "dbo.p": { "paths": { "[].shout": { "type": "ShoutSchema", "import": "@app/types", "why": "reverse of a name" } } } } }""").Document;

        Assert.Empty(doc.Unknowns);
        Assert.Equal(new IrImport("ShoutSchema", "@app/types"), Field(doc, "PRow", "shout").Type);
        Assert.Equal(new IrOverride("dbo.p", "results[0][].shout", IrOverrideSettings.Type, "reverse of a name"), Assert.Single(doc.Overrides));
        var zod = ZodEmitter.Emit(doc);
        Assert.Contains("import { ShoutSchema } from '@app/types'\n", zod, StringComparison.Ordinal);
        Assert.Contains("  shout: ShoutSchema.optional(),\n", zod, StringComparison.Ordinal);
    }

    [Fact]
    public void A_type_with_brackets_is_an_array_of_the_imported_schema()
    {
        var doc = Apply(GetCustomer, """{ "procedures": { "sales.get_customer": { "paths": { "[].lines": { "type": "LineSchema[]", "import": "@app/lines" } } } } }""").Document;

        Assert.Equal(new IrArray(new IrImport("LineSchema", "@app/lines")), Field(doc, "GetCustomerRow", "lines").Type);

        // The object type the override replaced is no longer reached, so it is not written.
        Assert.DoesNotContain("GetCustomerLine", doc.Types.Keys);
    }

    [Theory]
    [InlineData("./types/notes", "out", "../types/notes")]
    [InlineData("../shared/notes", "src/generated", "../../../shared/notes")]
    [InlineData("./types/notes", ".", "./types/notes")]
    [InlineData("@app/notes", "out", "@app/notes")]
    public void A_relative_import_is_written_relative_to_the_output_folder(string import, string output, string expected)
    {
        var doc = Apply(GetCustomer, $$"""{ "procedures": { "sales.get_customer": { "paths": { "[].notes": { "type": "NotesSchema", "import": "{{import}}" } } } } }""", output: output).Document;

        Assert.Equal(new IrImport("NotesSchema", expected), Field(doc, "GetCustomerRow", "notes").Type);
    }
}
