using Haruspex.SqlServer.Model.XmlSchemas;

namespace Haruspex.SqlServer.Semantics.XmlSchemas;

/// <summary>
/// Follows an <see cref="XmlPath"/> through a collection's declarations, counting the nodes it reaches: at least one when every step
/// is required, at most one when no step may repeat. SQL Server validates a typed instance against its collection on every write, so
/// a node the schemas require is there. Each rule was observed on the server (<c>XmlSchemaObservationTests</c>). Why:
/// docs/decisions/0139-xml-is-typed-from-its-schema-collection-where-the-schema-proves-it.md.
/// </summary>
/// <remarks>
/// <para>
/// A <c>DOCUMENT</c> instance has one top-level element, any global element of any schema in the collection, so its top-level step
/// is there only when the collection declares one global element that isn't abstract; a <c>CONTENT</c> instance may have none, or
/// many. A child element occurs as its parent's content model says: a sequence or an all group adds its parts, a choice takes its
/// least and its most, each scaled by the group's occurrences. An attribute is there when it is required or has a default or fixed
/// value, which the server writes into the instance.
/// </para>
/// <para>
/// Nothing is followed into an element that heads a substitution group or is abstract (a member may stand in its place), through
/// an element of a type an instance may replace (<c>xs:anyType</c>, an abstract type, or one another derives from, which
/// <c>xsi:type</c> may name), nor through content with an element wildcard, which may take an element of a declared name before the
/// declared one. An attribute wildcard doesn't stop a declared attribute, which the server still validates. An element an instance
/// may mark <c>xsi:nil</c> has no children then, so none below it is required, though its own attributes still are.
/// </para>
/// </remarks>
public static class XmlPaths
{
    /// <summary>Where <paramref name="path"/> reaches from <paramref name="from"/> in <paramref name="schemas"/>, or why nothing is proved of it.</summary>
    public static XmlReach Reach(XmlSchemaDeclarations schemas, XmlStart from, XmlPath path)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(path);

        var count = Count.One;
        XmlNode node;
        var steps = path.Steps.AsEnumerable();
        switch (from)
        {
            case XmlInstanceStart instance when path.Absolute && path.Steps is [{ IsAttribute: false } top, ..]:
            {
                // observed: A_document_s_root_is_any_global_element_of_any_schema_in_the_collection,
                // A_content_instance_may_be_empty_or_have_several_top_level_elements.
                if (schemas.Globals.FirstOrDefault(e => e.Name == top.Name) is not { } root)
                    return XmlReach.Unfollowed($"the collection declares no global element {top.Name}");
                if (Unreliable(root) is { } why)
                    return XmlReach.Unfollowed(why);
                var roots = schemas.Globals.Where(e => !e.Abstract).ToList();
                count = !instance.Document ? Count.Any : roots is [var only] && only == root ? Count.One : Count.Optional;
                if (top.First)
                    count = count.FirstOnly();
                node = new XmlNode(schemas.Types[root.Type], false, root.Nillable, top.Name.ToString());
                steps = steps.Skip(1);
                break;
            }

            case XmlInstanceStart:
                return XmlReach.Unfollowed("a path from an instance is followed only from its top level, by an element's name");
            case XmlNodeStart { Node: var start } when !path.Absolute:
                node = start;
                break;
            default:
                return XmlReach.Unfollowed("an absolute path from a node is not followed");
        }

        foreach (var step in steps)
        {
            if (node.IsAttribute)
                return XmlReach.Unfollowed("an attribute has no nodes below it");
            if (node.Type is not XmlComplexTypeDeclaration complex)
                return XmlReach.Unfollowed($"the element {node.Declaration} is of a simple type");

            // observed: An_instance_may_name_a_derived_type_in_xsi_type.
            if (complex.Replaceable)
                return XmlReach.Unfollowed($"an instance may give the element {node.Declaration} another type, with xsi:type");

            Count each;
            if (step.IsAttribute)
            {
                // observed: A_default_or_fixed_attribute_is_written_into_the_instance,
                // A_declared_attribute_is_validated_beside_an_attribute_wildcard.
                if (complex.Attributes.FirstOrDefault(a => a.Name == step.Name) is not { Use: not XmlAttributeUse.Prohibited } attribute)
                    return XmlReach.Unfollowed($"the element {node.Declaration} declares no attribute {step.Name}");
                each = attribute.Use is XmlAttributeUse.Required or XmlAttributeUse.Defaulted ? Count.One : Count.Optional;
                node = new XmlNode(schemas.Types[attribute.Type], true, false, $"{node.Declaration}/@{step.Name}");
            }
            else
            {
                // observed: A_wildcard_may_take_an_element_of_a_declared_name_before_it.
                if (complex.Particle is not { } particle)
                    return XmlReach.Unfollowed($"the element {node.Declaration} holds no elements");
                if (HasWildcard(particle))
                    return XmlReach.Unfollowed($"the content of the element {node.Declaration} has a wildcard");
                var declared = new List<XmlElementDeclaration>();
                each = Occurs(particle, step.Name, declared);
                if (declared.Count == 0)
                    return XmlReach.Unfollowed($"the element {node.Declaration} declares no child element {step.Name}");
                if (declared.Select(d => d.Type).Distinct().Skip(1).Any())
                    return XmlReach.Unfollowed($"the element {node.Declaration} declares child elements {step.Name} of different types");

                // observed: A_substitution_group_s_member_stands_where_its_head_is_declared.
                if (declared.Select(Unreliable).FirstOrDefault(w => w is not null) is { } unreliable)
                    return XmlReach.Unfollowed(unreliable);
                // A nil element has no children, though its type requires them (observed: A_nil_element_has_no_children_but_keeps_its_attributes).
                if (node.Nillable)
                    each = each with { Least = 0 };
                node = new XmlNode(schemas.Types[declared[0].Type], false, declared.Any(d => d.Nillable), $"{node.Declaration}/{step.Name}");
            }

            count = count.Times(step.First ? each.FirstOnly() : each);
        }

        if (path.First)
            count = count.FirstOnly();
        return new XmlReach(node, count.Least >= 1, count.Most <= 1, null);
    }

    /// <summary>Why a path can't be followed into an element by its name, or null when it can.</summary>
    private static string? Unreliable(XmlElementDeclaration element) =>
        element.Abstract ? $"the element {element.Name} is abstract"
        : element.HeadsGroup ? $"the element {element.Name} heads a substitution group"
        : null;

    private static bool HasWildcard(XmlParticle particle) => particle switch
    {
        XmlWildcardParticle => true,
        XmlGroupParticle group => group.Items.Any(HasWildcard),
        _ => false,
    };

    /// <summary>How many children named <paramref name="name"/> a particle holds, adding each declaration of that name to <paramref name="declared"/>.</summary>
    private static Count Occurs(XmlParticle particle, XmlName name, List<XmlElementDeclaration> declared)
    {
        Count own;
        switch (particle)
        {
            case XmlElementParticle { Element: var element } when element.Name == name:
                declared.Add(element);
                own = Count.One;
                break;
            case XmlGroupParticle { Kind: XmlGroupKind.Choice, Items: var items }:
            {
                var each = items.Select(p => Occurs(p, name, declared)).ToList();
                own = each.Count == 0 ? Count.None : new Count(each.Min(c => c.Least), each.Max(c => c.Most));
                break;
            }

            case XmlGroupParticle { Items: var items }:
                own = items.Select(p => Occurs(p, name, declared)).Aggregate(Count.None, (a, b) => a.Plus(b));
                break;
            default:
                return Count.None;
        }

        return own.Times(Count.Of(particle.MinOccurs, particle.MaxOccurs));
    }

    /// <summary>
    /// A number of nodes, from its least to its most, each counted only as far as a proof needs: none or at least one, and none, one
    /// or more than one. Counting so is exact under adding and multiplying, which only grow a count.
    /// </summary>
    private readonly record struct Count(int Least, int Most)
    {
        public static Count None => new(0, 0);

        public static Count One => new(1, 1);

        public static Count Optional => new(0, 1);

        public static Count Any => new(0, 2);

        public static Count Of(int least, int? most) => new(Math.Min(least, 1), most is { } m ? Math.Min(m, 2) : 2);

        public Count Plus(Count other) => new(Math.Min(1, Least + other.Least), Math.Min(2, Most + other.Most));

        public Count Times(Count other) => new(Math.Min(1, Least * other.Least), Math.Min(2, Most * other.Most));

        /// <summary>The count once <c>[1]</c> keeps only the first node.</summary>
        public Count FirstOnly() => new(Math.Min(Least, 1), Math.Min(Most, 1));
    }
}
