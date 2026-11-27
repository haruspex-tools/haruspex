using Haruspex.SqlServer.Model.XmlSchemas;
using Haruspex.SqlServer.Semantics.XmlSchemas;

namespace Haruspex.Tests.Semantics;

/// <summary>
/// <see cref="XmlPaths"/> over declarations built by hand, for what a compiled schema may already hide: .NET's compiler folds a
/// substitution group's members into the content model, so the rules that stop at a group's head or an abstract element are held
/// here on their own. Why: docs/decisions/0139-xml-is-typed-from-its-schema-collection-where-the-schema-proves-it.md.
/// </summary>
public class XmlPathsTests
{
    private static readonly XmlSimpleTypeDeclaration Int = new(new XmlName("http://www.w3.org/2001/XMLSchema", "int"), XmlValueKind.WholeNumber, new XmlFacets());

    /// <summary>A root <c>r</c> whose content is one required <paramref name="child"/>, of type 1, an int.</summary>
    private static XmlSchemaDeclarations Declarations(XmlElementDeclaration child)
    {
        var root = new XmlComplexTypeDeclaration(null, XmlContentKind.Elements, new XmlElementParticle(child, 1, 1), [], Replaceable: false);
        return new XmlSchemaDeclarations([new XmlElementDeclaration(new XmlName("", "r"), 0, false, false, false)], [root, Int]);
    }

    private static XmlReach Reach(XmlElementDeclaration child) =>
        XmlPaths.Reach(Declarations(child), new XmlInstanceStart(Document: true), XmlPath.Parse($"(/r/{child.Name.Local})[1]")!);

    [Fact]
    public void A_required_child_is_reached_in_a_document()
    {
        var reach = Reach(new XmlElementDeclaration(new XmlName("", "c"), 1, false, false, false));

        Assert.True(reach.Required);
        Assert.True(reach.Node!.HasValue);
    }

    [Fact]
    public void A_substitution_group_s_head_is_not_followed_though_the_content_model_names_it_alone()
    {
        // A_substitution_group_s_member_stands_where_its_head_is_declared.
        var reach = Reach(new XmlElementDeclaration(new XmlName("", "head"), 1, false, false, HeadsGroup: true));

        Assert.Null(reach.Node);
        Assert.Contains("heads a substitution group", reach.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void An_abstract_element_is_not_followed()
    {
        var reach = Reach(new XmlElementDeclaration(new XmlName("", "head"), 1, false, Abstract: true, false));

        Assert.Null(reach.Node);
        Assert.Contains("abstract", reach.Why, StringComparison.Ordinal);
    }
}
