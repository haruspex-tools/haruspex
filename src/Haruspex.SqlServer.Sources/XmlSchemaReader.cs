using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;
using Haruspex.SqlServer.Model.XmlSchemas;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// Reads an XML schema collection's schemas into the model's declarations (<see cref="XmlSchemaDeclarations"/>), compiling them
/// together as SQL Server does: each text may hold several <c>xs:schema</c> elements, and a schema may refer to another of the
/// collection by its namespace. .NET's XML Schema compiler reads them; a collection it refuses proves nothing, so it has no
/// declarations. Why: docs/decisions/0139-xml-is-typed-from-its-schema-collection-where-the-schema-proves-it.md.
/// </summary>
internal static partial class XmlSchemaReader
{
    private static readonly XmlQualifiedName AnyType = new("anyType", XmlSchema.Namespace);

    /// <summary>The declarations of <paramref name="texts"/>, compiled together; null when there are none or any fails to compile.</summary>
    public static XmlSchemaDeclarations? Read(IReadOnlyList<string> texts)
    {
        if (texts.Count == 0)
            return null;
        try
        {
            // No resolver: SQL Server reads no schema from elsewhere, and an import names a namespace of the collection.
            var set = new XmlSchemaSet { XmlResolver = null };
            var settings = new XmlReaderSettings
            {
                ConformanceLevel = ConformanceLevel.Fragment,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
            };
            foreach (var text in texts)
            {
                using var reader = XmlReader.Create(new StringReader(Declaration().Replace(text, "", 1)), settings);
                reader.MoveToContent();
                while (!reader.EOF)
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "schema" || reader.NamespaceURI != XmlSchema.Namespace)
                        return null;
                    using (var one = reader.ReadSubtree())
                    {
                        if (XmlSchema.Read(one, (_, e) => throw e.Exception) is not { } schema)
                            return null;
                        set.Add(schema);
                    }

                    reader.Read();
                    reader.MoveToContent();
                }
            }

            set.Compile();
            return new Declarations(set).Read();
        }
        catch (Exception e) when (e is XmlException or XmlSchemaException)
        {
            return null;
        }
    }

    /// <summary>An XML declaration at the start of a text, which a fragment may not have.</summary>
    [GeneratedRegex(@"^\s*<\?xml[^?]*\?>", RegexOptions.CultureInvariant)]
    private static partial Regex Declaration();

    /// <summary>The model's declarations of one compiled set: each type numbered once, so that a type its own content names is one entry.</summary>
    private sealed class Declarations
    {
        private readonly XmlSchemaSet set;
        private readonly HashSet<XmlQualifiedName> heads = [];
        private readonly HashSet<XmlSchemaType> derivedFrom = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<XmlSchemaType, int> numbers = new(ReferenceEqualityComparer.Instance);
        private readonly List<XmlTypeDeclaration?> types = [];

        public Declarations(XmlSchemaSet set)
        {
            this.set = set;
            foreach (var element in set.GlobalElements.Values.Cast<XmlSchemaElement>())
            {
                if (!element.SubstitutionGroup.IsEmpty)
                    heads.Add(element.SubstitutionGroup);
            }

            foreach (var type in set.GlobalTypes.Values.Cast<XmlSchemaType>())
            {
                for (var b = type.BaseXmlSchemaType; b is not null; b = b.BaseXmlSchemaType)
                    derivedFrom.Add(b);
            }
        }

        public XmlSchemaDeclarations Read()
        {
            var globals = set.GlobalElements.Values.Cast<XmlSchemaElement>().OrderBy(e => e.QualifiedName.Namespace, StringComparer.Ordinal).ThenBy(e => e.QualifiedName.Name, StringComparer.Ordinal).Select(Element).ToList();
            return new XmlSchemaDeclarations([.. globals], [.. types.Select(t => t ?? throw new InvalidOperationException("A type was numbered but not read."))]);
        }

        private XmlElementDeclaration Element(XmlSchemaElement element) =>
            new(Name(element.QualifiedName), Number(element.ElementSchemaType), element.IsNillable, element.IsAbstract, heads.Contains(element.QualifiedName));

        private int Number(XmlSchemaType? type)
        {
            type ??= XmlSchemaType.GetBuiltInComplexType(XmlTypeCode.Item);
            if (type is null)
                throw new InvalidOperationException("xs:anyType is built in.");
            if (numbers.TryGetValue(type, out var number))
                return number;
            number = types.Count;
            numbers[type] = number;
            types.Add(null);
            types[number] = type switch
            {
                XmlSchemaSimpleType simple => Simple(simple),
                XmlSchemaComplexType complex => Complex(complex),
                _ => throw new InvalidOperationException("A type is simple or complex."),
            };
            return number;
        }

        private XmlComplexTypeDeclaration Complex(XmlSchemaComplexType type)
        {
            var content = type.ContentType switch
            {
                XmlSchemaContentType.Empty => XmlContentKind.Empty,
                XmlSchemaContentType.TextOnly => XmlContentKind.Simple,
                XmlSchemaContentType.ElementOnly => XmlContentKind.Elements,
                _ => XmlContentKind.Mixed,
            };
            var attributes = type.AttributeUses.Values.Cast<XmlSchemaAttribute>()
                .OrderBy(a => a.QualifiedName.Namespace, StringComparer.Ordinal).ThenBy(a => a.QualifiedName.Name, StringComparer.Ordinal)
                .Select(a => new XmlAttributeDeclaration(
                    Name(a.QualifiedName),
                    Number(a.AttributeSchemaType),
                    a.Use switch
                    {
                        XmlSchemaUse.Required => XmlAttributeUse.Required,
                        XmlSchemaUse.Prohibited => XmlAttributeUse.Prohibited,
                        _ when a.DefaultValue is not null || a.FixedValue is not null => XmlAttributeUse.Defaulted,
                        _ => XmlAttributeUse.Optional,
                    }))
                .ToList();
            var replaceable = type.QualifiedName == AnyType || type.IsAbstract || derivedFrom.Contains(type);
            var particle = content is XmlContentKind.Elements or XmlContentKind.Mixed ? Particle(type.ContentTypeParticle) : null;
            return new XmlComplexTypeDeclaration(NameOrNull(type.QualifiedName), content, particle, [.. attributes], replaceable);
        }

        private XmlParticle? Particle(XmlSchemaParticle particle)
        {
            var min = particle.MinOccurs >= int.MaxValue ? int.MaxValue : (int)particle.MinOccurs;
            int? max = particle.MaxOccurs >= int.MaxValue ? null : (int)particle.MaxOccurs;
            return particle switch
            {
                XmlSchemaElement element => new XmlElementParticle(Element(element), min, max),
                XmlSchemaSequence or XmlSchemaChoice or XmlSchemaAll when ((XmlSchemaGroupBase)particle).Items.Count == 0 && particle is not XmlSchemaChoice => null,
                XmlSchemaGroupBase group => new XmlGroupParticle(
                    group switch { XmlSchemaChoice => XmlGroupKind.Choice, XmlSchemaAll => XmlGroupKind.All, _ => XmlGroupKind.Sequence },
                    [.. group.Items.OfType<XmlSchemaParticle>().Select(p => Particle(p) ?? new XmlGroupParticle(XmlGroupKind.Sequence, [], 1, 1))],
                    min,
                    max),

                // A wildcard, or anything else the compiler gives, which a path can't follow by name.
                _ => new XmlWildcardParticle(min, max),
            };
        }

        private static XmlSimpleTypeDeclaration Simple(XmlSchemaSimpleType type)
        {
            if (type.Datatype is not { Variety: XmlSchemaDatatypeVariety.Atomic })
                return new XmlSimpleTypeDeclaration(NameOrNull(type.QualifiedName), XmlValueKind.Other, new XmlFacets());

            var chain = new List<XmlSchemaSimpleTypeRestriction>();
            for (XmlSchemaType? t = type; t is XmlSchemaSimpleType simple; t = simple.BaseXmlSchemaType)
            {
                if (simple.Content is XmlSchemaSimpleTypeRestriction restriction)
                    chain.Add(restriction);
            }

            var facets = chain.SelectMany(r => r.Facets.OfType<XmlSchemaFacet>()).ToList();
            var whiteSpace = facets.OfType<XmlSchemaWhiteSpaceFacet>().FirstOrDefault()?.Value;
            var kind = type.TypeCode switch
            {
                XmlTypeCode.String => whiteSpace is null or "preserve" ? XmlValueKind.Text : XmlValueKind.NormalisedText,
                XmlTypeCode.NormalizedString or XmlTypeCode.Token or XmlTypeCode.Language or XmlTypeCode.NmToken or XmlTypeCode.Name
                    or XmlTypeCode.NCName or XmlTypeCode.Id or XmlTypeCode.Idref or XmlTypeCode.Entity => XmlValueKind.NormalisedText,
                XmlTypeCode.Decimal => XmlValueKind.DecimalNumber,
                _ when BuiltInRange(type.TypeCode) is not null => XmlValueKind.WholeNumber,
                _ => XmlValueKind.Other,
            };
            if (kind == XmlValueKind.Other)
                return new XmlSimpleTypeDeclaration(NameOrNull(type.QualifiedName), kind, new XmlFacets(Pattern: facets.OfType<XmlSchemaPatternFacet>().Any()));

            // Whitespace is replaced in a normalizedString and collapsed below it, unless a facet says otherwise; an enumeration's
            // values are compared after it, so they are given so.
            var mode = whiteSpace ?? (type.TypeCode switch { XmlTypeCode.String => "preserve", XmlTypeCode.NormalizedString => "replace", _ => "collapse" });
            var enumeration = chain.Select(r => r.Facets.OfType<XmlSchemaEnumerationFacet>().Select(f => Normalised(f.Value ?? "", mode)).ToList()).FirstOrDefault(e => e.Count > 0);
            var numeric = kind is XmlValueKind.WholeNumber or XmlValueKind.DecimalNumber;
            var range = BuiltInRange(type.TypeCode);

            string? Tightest<T>(bool lower, string? builtIn) where T : XmlSchemaFacet =>
                !numeric ? null
                : facets.OfType<T>().Select(f => f.Value).Append(builtIn).OfType<string>()
                    .Select(v => (Text: v, Ok: decimal.TryParse(v, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n), Number: n))
                    .Where(v => v.Ok)
                    .OrderBy(v => lower ? -v.Number : v.Number)
                    .Select(v => v.Text)
                    .FirstOrDefault();
            int? Least<T>() where T : XmlSchemaFacet =>
                facets.OfType<T>().Select(f => int.TryParse(f.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : (int?)null).Where(n => n is not null).Min();
            int? Most<T>() where T : XmlSchemaFacet =>
                facets.OfType<T>().Select(f => int.TryParse(f.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : (int?)null).Where(n => n is not null).Max();

            var length = Least<XmlSchemaLengthFacet>();
            var maxLength = new[] { Least<XmlSchemaMaxLengthFacet>(), length }.Where(n => n is not null).Min();
            var minLength = new[] { Most<XmlSchemaMinLengthFacet>(), length }.Where(n => n is not null).Max();
            return new XmlSimpleTypeDeclaration(
                NameOrNull(type.QualifiedName),
                kind,
                new XmlFacets(
                    Enumeration: enumeration is null ? null : [.. enumeration],
                    MinInclusive: Tightest<XmlSchemaMinInclusiveFacet>(lower: true, range?.Least),
                    MinExclusive: Tightest<XmlSchemaMinExclusiveFacet>(lower: true, null),
                    MaxInclusive: Tightest<XmlSchemaMaxInclusiveFacet>(lower: false, range?.Greatest),
                    MaxExclusive: Tightest<XmlSchemaMaxExclusiveFacet>(lower: false, null),
                    MinLength: numeric ? null : minLength,
                    MaxLength: numeric ? null : maxLength,
                    TotalDigits: numeric ? Least<XmlSchemaTotalDigitsFacet>() : null,
                    FractionDigits: numeric ? Least<XmlSchemaFractionDigitsFacet>() : null,
                    Pattern: facets.OfType<XmlSchemaPatternFacet>().Any()));
        }

        /// <summary>A value as the whitespace rule <paramref name="mode"/> gives it: kept, each tab and line end a space, or those collapsed.</summary>
        private static string Normalised(string value, string mode)
        {
            if (mode == "preserve")
                return value;
            var replaced = value.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
            return mode == "replace" ? replaced : string.Join(' ', replaced.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>The range XML Schema gives a built-in whole-number type; null for a type that isn't one.</summary>
        private static (string? Least, string? Greatest)? BuiltInRange(XmlTypeCode code) => code switch
        {
            XmlTypeCode.Integer => (null, null),
            XmlTypeCode.NonPositiveInteger => (null, "0"),
            XmlTypeCode.NegativeInteger => (null, "-1"),
            XmlTypeCode.Long => ("-9223372036854775808", "9223372036854775807"),
            XmlTypeCode.Int => ("-2147483648", "2147483647"),
            XmlTypeCode.Short => ("-32768", "32767"),
            XmlTypeCode.Byte => ("-128", "127"),
            XmlTypeCode.NonNegativeInteger => ("0", null),
            XmlTypeCode.UnsignedLong => ("0", "18446744073709551615"),
            XmlTypeCode.UnsignedInt => ("0", "4294967295"),
            XmlTypeCode.UnsignedShort => ("0", "65535"),
            XmlTypeCode.UnsignedByte => ("0", "255"),
            XmlTypeCode.PositiveInteger => ("1", null),
            _ => null,
        };

        private static XmlName Name(XmlQualifiedName name) => new(name.Namespace, name.Name);

        private static XmlName? NameOrNull(XmlQualifiedName name) => name.IsEmpty ? null : Name(name);
    }
}
