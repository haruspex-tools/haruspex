using System.Globalization;
using System.Text.Json;

namespace Haruspex.Ir;

/// <summary>
/// One JSON object of an IR file as the reader walks it: its properties, the path to it, and which properties have been
/// read. Every accessor names the path in what it throws, and <see cref="End"/> refuses a property nothing read, so the
/// format stays closed: a key the reader doesn't know is an error, never ignored.
/// </summary>
/// <remarks>
/// Nothing here supplies a default. A property the format requires is required; one it leaves out when it says nothing
/// (an optional property) is read as absent, which is what the writer meant by leaving it out.
/// </remarks>
internal sealed class IrJsonObject
{
    private readonly Dictionary<string, JsonElement> properties = new(StringComparer.Ordinal);
    private readonly HashSet<string> read = new(StringComparer.Ordinal);

    private IrJsonObject(JsonElement element, string path)
    {
        Path = path;
        foreach (var property in element.EnumerateObject())
        {
            var name = Text(() => property.Name, path, "has a key that");

            // System.Text.Json keeps both of two properties with one name; a document that says two things is refused.
            if (!properties.TryAdd(name, property.Value))
                throw new IrFormatException(PathOf(name), "appears twice.");
        }
    }

    /// <summary>The path to this object from the document's root.</summary>
    public string Path { get; }

    /// <summary>Reads <paramref name="element"/>, which must be an object, as the object at <paramref name="path"/>.</summary>
    public static IrJsonObject At(JsonElement element, string path) =>
        element.ValueKind == JsonValueKind.Object ? new(element, path) : throw new IrFormatException(path, "must be an object.");

    /// <summary>The path to the property <paramref name="name"/> of this object.</summary>
    public string PathOf(string name) => Path + IrPath.Key(name);

    /// <summary>The names of the object's properties, in the file's order; listing them reads none.</summary>
    public IEnumerable<string> Names => properties.Keys;

    /// <summary>Whether the object has the property <paramref name="name"/>; asking does not read it.</summary>
    public bool Has(string name) => properties.ContainsKey(name);

    /// <summary>The property <paramref name="name"/>, which the format requires.</summary>
    public JsonElement Required(string name) =>
        Optional(name) ?? throw new IrFormatException(PathOf(name), "is missing.");

    /// <summary>The property <paramref name="name"/>, or null when the object leaves it out.</summary>
    public JsonElement? Optional(string name)
    {
        read.Add(name);
        return properties.TryGetValue(name, out var value) ? value : null;
    }

    /// <summary>A required string.</summary>
    public string String(string name) => AsString(Required(name), PathOf(name));

    /// <summary>An optional string, null when left out.</summary>
    public string? OptionalString(string name) => Optional(name) is { } value ? AsString(value, PathOf(name)) : null;

    /// <summary>A required SHA-256, as the IR writes one: 64 lower-case hexadecimal digits, as the schema's <c>pattern</c> says.</summary>
    public string Sha256(string name) => AsSha256(Required(name), PathOf(name));

    /// <summary>An optional SHA-256, null when left out.</summary>
    public string? OptionalSha256(string name) => Optional(name) is { } value ? AsSha256(value, PathOf(name)) : null;

    /// <summary>A required string that is not empty, as the schema's <c>minLength</c> of 1 says.</summary>
    public string NonEmptyString(string name)
    {
        var text = String(name);
        return text.Length > 0 ? text : throw new IrFormatException(PathOf(name), "is empty.");
    }

    /// <summary>A required <c>true</c> or <c>false</c>.</summary>
    public bool Boolean(string name) => AsBoolean(Required(name), PathOf(name));

    /// <summary>An optional <c>true</c> or <c>false</c>, null when left out.</summary>
    public bool? OptionalBoolean(string name) => Optional(name) is { } value ? AsBoolean(value, PathOf(name)) : null;

    /// <summary>A required count: a length, a number of digits or of items, from 0 to <see cref="int.MaxValue"/>.</summary>
    public int Count(string name) => AsCount(Required(name), PathOf(name));

    /// <summary>An optional count, null when left out.</summary>
    public int? OptionalCount(string name) => Optional(name) is { } value ? AsCount(value, PathOf(name)) : null;

    /// <summary>A required object.</summary>
    public IrJsonObject Object(string name) => At(Required(name), PathOf(name));

    /// <summary>An optional object, null when left out.</summary>
    public IrJsonObject? OptionalObject(string name) => Optional(name) is { } value ? At(value, PathOf(name)) : null;

    /// <summary>A required array, as its items with the path to each.</summary>
    public IEnumerable<(JsonElement Item, string Path)> Array(string name) => Items(Required(name), PathOf(name));

    /// <summary>A required name from a closed vocabulary, read with that vocabulary's table in <see cref="IrWireNames"/>.</summary>
    /// <param name="name">The property.</param>
    /// <param name="parse">The vocabulary's <c>IrWireNames.TryParse</c>.</param>
    /// <param name="what">What the vocabulary names, for the message: <c>a null rule</c>.</param>
    public T Name<T>(string name, IrWireNameParser<T> parse, string what)
        where T : struct, Enum => AsName(Required(name), PathOf(name), parse, what);

    /// <summary>An optional name from a closed vocabulary, null when left out.</summary>
    public T? OptionalName<T>(string name, IrWireNameParser<T> parse, string what)
        where T : struct, Enum => Optional(name) is { } value ? AsName(value, PathOf(name), parse, what) : null;

    /// <summary>
    /// Ends the reading of this object: a property nothing asked for is not part of the format here, so the file is refused
    /// rather than half read.
    /// </summary>
    public void End()
    {
        foreach (var name in properties.Keys.Where(n => !read.Contains(n)).Order(StringComparer.Ordinal))
            throw new IrFormatException(PathOf(name), "is not a property the format has here.");
    }

    /// <summary>An array's items, with the path to each.</summary>
    public static IEnumerable<(JsonElement Item, string Path)> Items(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new IrFormatException(path, "must be an array.");
        return element.EnumerateArray().Select((item, i) => (item, path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]"));
    }

    /// <summary>A string, or an error naming <paramref name="path"/>.</summary>
    public static string AsString(JsonElement element, string path) =>
        // Never null: GetString is null only for a JSON null, and the element is a string.
        element.ValueKind == JsonValueKind.String ? Text(() => element.GetString()!, path, "is a string that") : throw new IrFormatException(path, "must be a string.");

    /// <summary>
    /// Text of the file as a .NET string: a key or a string value. JSON may escape half of a surrogate pair alone (<c>"\ud800"</c>),
    /// which is well-formed JSON but no text, so the parser accepts it and only turning it into a string fails; that failure is
    /// the file's, at <paramref name="path"/>, not the reader's.
    /// </summary>
    /// <param name="read">Turns the JSON into a string: <c>GetString</c> or a property's <c>Name</c>.</param>
    /// <param name="path">Where the text is: the value's path, or the object's for a key.</param>
    /// <param name="what">The start of the message, which says which: <c>is a string that</c>, <c>has a key that</c>.</param>
    private static string Text(Func<string> read, string path, string what)
    {
        try
        {
            return read();
        }
        catch (InvalidOperationException e)
        {
            throw new IrFormatException(path, $"{what} is not text: it escapes half of a UTF-16 surrogate pair alone.", e);
        }
    }

    /// <summary>
    /// The optional property <paramref name="name"/> as a whole commit: 40 lower-case hexadecimal digits, or 64 in a repository that
    /// names commits by SHA-256; null when absent.
    /// </summary>
    public string? OptionalCommit(string name)
    {
        if (OptionalString(name) is not { } text)
            return null;
        return text.Length is 40 or 64 && text.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f')
            ? text
            : throw new IrFormatException(PathOf(name), $"is \"{text}\", which is not a whole commit as the IR writes one: 40 or 64 lower-case hexadecimal digits.");
    }

    private static string AsSha256(JsonElement element, string path)
    {
        var text = AsString(element, path);
        return text.Length == 64 && text.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f')
            ? text
            : throw new IrFormatException(path, $"is \"{text}\", which is not a SHA-256 as the IR writes one: 64 lower-case hexadecimal digits.");
    }

    private static bool AsBoolean(JsonElement element, string path) => element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new IrFormatException(path, "must be true or false."),
    };

    /// <summary>Every integer in the IR counts something, so none is negative; the schema says the same (<c>minimum</c>, <c>maximum</c>).</summary>
    private static int AsCount(JsonElement element, string path) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value) && value >= 0
            ? value
            : throw new IrFormatException(path, $"must be a whole number from 0 to {int.MaxValue.ToString(CultureInfo.InvariantCulture)}.");

    private static T AsName<T>(JsonElement element, string path, IrWireNameParser<T> parse, string what)
        where T : struct, Enum
    {
        var text = AsString(element, path);
        return parse(text, out var value)
            ? value
            : throw new IrFormatException(path, $"is \"{text}\", which is not {what} in IR {IrDocument.CurrentVersion}.");
    }
}
