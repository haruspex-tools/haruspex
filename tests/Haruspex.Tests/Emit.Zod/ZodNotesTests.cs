using Haruspex.Emit.Zod.Model;
using static Haruspex.Emit.Zod.Model.ZodExpression;

namespace Haruspex.Tests.Emit.Zod;

/// <summary>How the notes inside an expression become the one text written beside its property.</summary>
public class ZodNotesTests
{
    [Fact]
    public void An_expression_without_notes_has_none() => Assert.Null(ZodNotes.Of(Z("string").Then("max", new Number("10"))));

    [Fact]
    public void A_note_comes_before_the_notes_of_what_it_is_about() =>
        Assert.Equal("JSON text; an upper bound", ZodNotes.Of(new Noted(new JsonText(new Noted(Z("string"), "an upper bound")), "JSON text")));

    [Fact]
    public void A_note_survives_the_checks_chained_after_it() =>
        Assert.Equal("an upper bound", ZodNotes.Of(new Noted(Z("string"), "an upper bound").Then("nullable")));

    [Fact]
    public void The_parts_notes_follow_in_order_each_distinct_text_once() =>
        Assert.Equal(
            "first; second",
            ZodNotes.Of(Z("tuple", new ArrayLiteral([new Noted(Z("string"), "first"), new Noted(Z("string"), "second"), new Noted(Z("number"), "first")]))));

    [Fact]
    public void An_object_schema_leaves_its_properties_notes_to_them() =>
        Assert.Null(ZodNotes.Of(new ObjectSchema([new ZodProperty("v", new Noted(Z("string"), "a note"))], Strict: false)));
}
