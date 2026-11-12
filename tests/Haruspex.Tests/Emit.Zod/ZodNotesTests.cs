using Haruspex.Emit.Zod.Model;
using static Haruspex.Emit.Zod.Model.ZodExpression;

namespace Haruspex.Tests.Emit.Zod;

/// <summary>How the notes inside an expression are gathered for the property that holds it.</summary>
public class ZodNotesTests
{
    private static readonly ZodNote First = new("First.");
    private static readonly ZodNote Second = new("Second.", ZodCaveat.ByteLengths);

    [Fact]
    public void An_expression_without_notes_has_none() => Assert.Empty(ZodNotes.Of(Z("string").Then("max", new Number("10"))));

    [Fact]
    public void A_note_comes_before_the_notes_of_what_it_is_about() =>
        Assert.Equal([First, Second], ZodNotes.Of(new Noted(new JsonText(new Noted(Z("string"), Second)), First)));

    [Fact]
    public void A_note_survives_the_checks_chained_after_it() =>
        Assert.Equal([First], ZodNotes.Of(new Noted(Z("string"), First).Then("nullable")));

    [Fact]
    public void The_parts_notes_follow_in_order_each_once() =>
        Assert.Equal(
            [First, Second],
            ZodNotes.Of(Z("tuple", new ArrayLiteral([new Noted(Z("string"), First), new Noted(Z("string"), Second), new Noted(Z("number"), First)]))));

    [Fact]
    public void An_object_schema_leaves_its_properties_notes_to_them() =>
        Assert.Empty(ZodNotes.Of(new ObjectSchema([new ZodProperty("v", new Noted(Z("string"), First))], Strict: false)));
}
