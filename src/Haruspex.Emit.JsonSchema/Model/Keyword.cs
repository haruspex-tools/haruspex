namespace Haruspex.Emit.JsonSchema.Model;

/// <summary>
/// The JSON Schema 2020-12 keywords the emitter writes, and the order the printer writes them in: identity first, then what a
/// value is, then its parts, so a reader meets a property's type before its details.
/// </summary>
internal static class Keyword
{
    public const string Schema = "$schema";
    public const string Ref = "$ref";
    public const string Title = "title";
    public const string Description = "description";
    public const string Type = "type";
    public const string Enum = "enum";
    public const string Format = "format";
    public const string Pattern = "pattern";
    public const string MinLength = "minLength";
    public const string MaxLength = "maxLength";
    public const string Minimum = "minimum";
    public const string Maximum = "maximum";
    public const string ContentEncoding = "contentEncoding";
    public const string ContentMediaType = "contentMediaType";
    public const string ContentSchema = "contentSchema";
    public const string Properties = "properties";
    public const string Required = "required";
    public const string AdditionalProperties = "additionalProperties";
    public const string PrefixItems = "prefixItems";
    public const string Items = "items";
    public const string MinItems = "minItems";
    public const string MaxItems = "maxItems";
    public const string AnyOf = "anyOf";
    public const string Not = "not";
    public const string Definitions = "$defs";

    /// <summary>What a reference to a definition of the same document starts with: <c>#/$defs/</c>.</summary>
    public const string DefinitionsPointer = "#/$defs/";

    /// <summary>The draft every document is written in.</summary>
    public const string Draft = "https://json-schema.org/draft/2020-12/schema";

    /// <summary>The order keywords are written in; a keyword not listed is a fault in the emitter, not something to write last.</summary>
    public static IReadOnlyList<string> Order { get; } =
    [
        Schema, Ref, Title, Description, Type, Enum, Format, Pattern, MinLength, MaxLength, Minimum, Maximum, ContentEncoding, ContentMediaType,
        ContentSchema, Properties, Required, AdditionalProperties, PrefixItems, Items, MinItems, MaxItems, AnyOf, Not, Definitions,
    ];
}
