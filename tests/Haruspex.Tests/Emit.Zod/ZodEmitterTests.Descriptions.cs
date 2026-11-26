using Haruspex.Emit.JsonSchema;
using Haruspex.Emit.Mssql;
using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests.Emit.Zod;

/// <summary>
/// A description is text from the database, so nothing in it may break what it is written into: a comment, a string, a JSON
/// value, nor become a tag an editor acts on (docs/decisions/0128-a-column-s-description-reaches-the-fields-that-are-that-column.md).
/// </summary>
public partial class ZodEmitterTests
{
    /// <summary>A description that holds each thing that could break a comment or a string, or be read as a tag.</summary>
    private const string Hazards = "Ends */ here, \"quoted\", with a \\ and a\u2028line separator.\n@deprecated not a tag\n\n  ";

    private static IrDocument Described() => InferProcedure.Infer(
        "shop.*",
        ("tables.sql", OrderTables + "\nGO\nEXEC sp_addextendedproperty 'MS_Description', N'" + Hazards.Replace("'", "''", StringComparison.Ordinal) + "', 'SCHEMA', 'shop', 'TABLE', 'customer_order', 'COLUMN', 'note';"),
        ("procedures.sql", "CREATE PROCEDURE shop.get_note @order_id int AS SELECT o.note FROM shop.customer_order AS o WHERE o.order_id = @order_id;\nGO\n"
            + "EXEC sp_addextendedproperty 'MS_Description', N'" + Hazards.Replace("'", "''", StringComparison.Ordinal) + "', 'SCHEMA', 'shop', 'PROCEDURE', 'get_note';"));

    [Fact]
    public void A_description_s_comment_escapes_what_would_end_it_or_become_a_tag_and_its_string_keeps_it_whole()
    {
        var document = Described();
        var ts = ZodEmitter.Emit(document).Expected();

        Assert.Contains(
            """
              /**
               * Ends *\/ here, "quoted", with a \ and a line separator.
               * \@deprecated not a tag
               */
              note: z.string().max(40).nullable().describe("Ends */ here, \"quoted\", with a \\ and a\u2028line separator.\n@deprecated not a tag\n\n  "),
            """.ReplaceLineEndings("\n"),
            ts,
            StringComparison.Ordinal);
        Assert.Contains("/**\n * Ends *\\/ here, \"quoted\", with a \\ and a line separator.\n * \\@deprecated not a tag\n * shop.get_note: returns rows.\n */\n", ts, StringComparison.Ordinal);
        Assert.Equal(Hazards, document.Types.Values.Single().Fields.Single().Description);
    }

    [Fact]
    public void A_procedure_s_description_heads_its_function_s_comment_escaped_as_the_module_s()
    {
        var ts = MssqlEmitter.Emit(Described()).Expected();

        Assert.Contains("/**\n * Ends *\\/ here, \"quoted\", with a \\ and a line separator.\n * \\@deprecated not a tag\n * Calls shop.get_note.\n */\nexport async function", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void A_description_is_json_schema_s_first_paragraph_as_the_database_gives_it()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSchemaEmitter.Emit(Described()).Expected())!;

        var note = json["$defs"]!["GetNoteRow"]!["properties"]!["note"]!["description"]!.GetValue<string>();
        Assert.StartsWith(Hazards + "\n\nSQL type nvarchar(40).", note, StringComparison.Ordinal);
    }
}
