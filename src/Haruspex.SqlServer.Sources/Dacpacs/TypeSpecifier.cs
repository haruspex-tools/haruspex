using System.Globalization;
using System.Xml.Linq;
using Haruspex.SqlServer.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Sources.Dacpacs;

/// <summary>
/// A type as <c>model.xml</c> gives one, as the model's <see cref="SqlType"/>: a reference to the type, and its length, precision
/// and scale as properties beside it. DacFx has already resolved what was written, so the arguments are the type's whole, never
/// left to a default.
/// </summary>
/// <remarks>
/// <para>
/// DacFx leaves out a property at its zero: <c>datetime2(0)</c> has no <c>Scale</c>, while <c>datetime2</c> (seven places) has
/// <c>Scale</c> 7, and <c>decimal(10)</c> has no <c>Scale</c> (observed in <c>tests/dacpacs</c>: the <c>orders</c> fixture's
/// <c>@before datetime2(0)</c>, the <c>atelier</c> project's <c>valid_from datetime2</c>). So a type that takes a scale is given
/// it, 0 when absent, and a decimal its precision and scale both. A model read from a folder keeps what was written instead
/// (<c>datetime2</c>, arguments empty); the two resolve to the same type in Semantics, which is what every reader asks of them.
/// </para>
/// <para>
/// A type outside <c>sys</c> is a user type, by its two-part name. One in <c>sys</c> is built in when ScriptDom gives it a
/// keyword of its own, and otherwise named, as <c>sysname</c>, <c>geography</c> and <c>hierarchyid</c> are when a folder's
/// script declares them (<c>SyntaxReader.Type</c>).
/// </para>
/// </remarks>
internal static class TypeSpecifier
{
    private static readonly HashSet<string> Scaled = new(StringComparer.Ordinal) { "datetime2", "datetimeoffset", "time" };

    private static readonly HashSet<string> Exact = new(StringComparer.Ordinal) { "decimal", "numeric" };

    /// <summary>The type an element names through <paramref name="relationship"/> (a column's <c>TypeSpecifier</c>, a parameter's <c>Type</c>), or null when it names none.</summary>
    public static SqlType? Of(XElement element, string relationship) =>
        ModelXml.Elements(element, relationship).FirstOrDefault() is { } specifier ? Read(specifier) : null;

    /// <summary>The type a specifier (or an alias type's own element, which carries the same properties) names.</summary>
    public static SqlType? Read(XElement specifier)
    {
        if (ModelXml.References(specifier, ModelXml.Type).FirstOrDefault() is not { } reference || ModelXml.NameOf(reference) is not { } name)
            return null;
        var parts = ElementName.Parts(name);
        var builtIn = (string?)reference.Attribute("ExternalSource") == "BuiltIns" && parts.Count == 1;
        if (!builtIn)
            return parts is [var schema, var type] ? new SqlType(SqlTypeKind.Named, type, [], schema) : null;

#pragma warning disable CA1308 // A canonical name, as sys.types gives it, not a comparison.
        var system = parts[0].ToLowerInvariant();
#pragma warning restore CA1308
        // Typed by a collection, the specifier names it; XmlStyle 2 is DOCUMENT, and CONTENT, the default, is left out.
        if (system == "xml")
        {
            return new SqlType(SqlTypeKind.Xml, system, [])
            {
                Collection = ModelXml.References(specifier, ModelXml.XmlSchemaCollection).FirstOrDefault() is { } collection
                    && ModelXml.NameOf(collection) is { } named && ElementName.Parts(named) is [var schema, var collectionName]
                    ? new XmlCollectionReference(collectionName, schema, ModelXml.Property(specifier, ModelXml.XmlStyle) == "2")
                    : null,
            };
        }
        return Enum.TryParse<SqlDataTypeOption>(system, ignoreCase: true, out _) && system != "none"
            ? new SqlType(SqlTypeKind.BuiltIn, system, Arguments(specifier, system))
            : new SqlType(SqlTypeKind.Named, system, []);
    }

    private static Collections.EquatableArray<string> Arguments(XElement specifier, string type)
    {
        string? Property(string name) => ModelXml.Property(specifier, name);
        if (ModelXml.IsTrue(specifier, ModelXml.IsMax))
            return ["max"];
        if (Property(ModelXml.Length) is { } length)
            return [length];
        if (Exact.Contains(type))
            return [Property(ModelXml.Precision) ?? "18", Property(ModelXml.Scale) ?? "0"];
        if (Scaled.Contains(type))
            return [Property(ModelXml.Scale) ?? "0"];
        // float is float(53), which a folder writes as float; DacFx writes its precision.
        return Property(ModelXml.Precision) is { } precision && !(type == "float" && precision == "53") ? [precision] : [];
    }

    /// <summary>A number property as an integer, for the few places one is counted.</summary>
    public static int? Number(XElement element, string name) =>
        int.TryParse(ModelXml.Property(element, name), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
}
