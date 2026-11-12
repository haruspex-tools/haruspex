using Haruspex.Emit.Zod.Lowering;
using Haruspex.Emit.Zod.Model;
using static Haruspex.Emit.Zod.Model.ZodExpression;

namespace Haruspex.Tests.Emit.Zod;

/// <summary>
/// When one union variant accepts every value of another, so the other can go, and above all when it doesn't: each guard
/// in <see cref="Unions.Covers"/> is a check that would be dropped without it.
/// </summary>
public class UnionsTests
{
    private static readonly Dictionary<string, (ZodExpression Wider, ZodExpression Narrower)> Covered = new()
    {
        ["an equal variant"] = (Z("string").Then("max", new Number("5")), Z("string").Then("max", new Number("5"))),
        ["a bare number over a bounded int"] = (Z("number"), Z("int").Then("min", new Number("0")).Then("max", new Number("255"))),
        ["a bare number over a literal"] = (Z("number"), Z("literal", new ArrayLiteral([new Number("1")]))),
        ["a longer maximum over a shorter one"] = (Z("string").Then("max", new Number("60")), Z("string").Then("max", new Number("13"))),
        ["a maximum over an enum that fits it"] = (Z("string").Then("max", new Number("5")), Z("enum", new ArrayLiteral([new Text("SPARE")]))),
        ["a bare string over a string with checks"] = (Z("string"), Z("string").Then("min", new Number("1")).Then("regex", new PaddedValues(["a"]))),
        ["a note, which changes nothing accepted"] = (new Noted(Z("string").Then("max", new Number("8")), new ZodNote("A note.")), Z("string").Then("max", new Number("8"))),
    };

    private static readonly Dictionary<string, (ZodExpression Wider, ZodExpression Narrower)> NotCovered = new()
    {
        ["a bounded number over an int"] = (Z("number").Then("min", new Number("0")), Z("int")),
        ["a maximum over an enum value longer than it"] = (Z("string").Then("max", new Number("3")), Z("enum", new ArrayLiteral([new Text("long")]))),
        ["a string with a pattern over a shorter string"] = (Z("string").Then("regex", new PaddedValues(["a"])), Z("string").Then("max", new Number("1"))),
        ["a string with a minimum over a shorter string"] = (Z("string").Then("min", new Number("2")).Then("max", new Number("9")), Z("string").Then("max", new Number("1"))),
        ["a string over a nullable one"] = (Z("string"), Z("string").Then("nullable")),
        ["a number over an optional int"] = (Z("number"), Z("int").Then("optional")),
        ["a nullable number over an int"] = (Z("number").Then("nullable"), Z("int")),
        ["a shorter maximum over a longer one"] = (Z("string").Then("max", new Number("13")), Z("string").Then("max", new Number("60"))),
        ["a maximum over a string with no maximum"] = (Z("string").Then("max", new Number("13")), Z("string")),
        ["a number over a string"] = (Z("number"), Z("string")),
        ["an object over a different object"] = (new Name("ASchema"), new Name("BSchema")),
    };

    public static TheoryData<string> CoveredNames => [.. Covered.Keys];

    public static TheoryData<string> NotCoveredNames => [.. NotCovered.Keys];

    [Theory]
    [MemberData(nameof(CoveredNames))]
    public void A_variant_another_accepts_every_value_of_is_covered(string name) =>
        Assert.True(Unions.Covers(Covered[name].Wider, Covered[name].Narrower));

    [Theory]
    [MemberData(nameof(NotCoveredNames))]
    public void A_variant_with_a_value_the_other_refuses_is_not_covered(string name) =>
        Assert.False(Unions.Covers(NotCovered[name].Wider, NotCovered[name].Narrower));

    [Fact]
    public void Of_two_equal_variants_the_first_stays_and_a_union_of_one_is_that_variant() =>
        Assert.Equal(Z("number"), Unions.Of([Z("number"), Z("number")]));

    [Fact]
    public void Variants_neither_of_which_covers_the_other_stay_in_order() =>
        Assert.Equal(new Union([Z("string"), Z("number")]), Unions.Of([Z("string"), Z("number")]));
}
