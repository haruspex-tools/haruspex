using System.Globalization;

namespace Haruspex.Config;

/// <summary>
/// A JSON Pointer (RFC 6901) into a document, as a <c>$ref</c>'s fragment or a declaration's path after its <c>#</c> writes one:
/// <c>/$defs/person</c>, each token naming a key of an object or an index of an array, <c>~1</c> standing for <c>/</c> and
/// <c>~0</c> for <c>~</c>. A fragment is part of a URI, so it is percent-decoded first.
/// </summary>
internal static class JsonPointer
{
    /// <summary>
    /// The value <paramref name="pointer"/> names in <paramref name="root"/>, with its place as problems write it from
    /// <paramref name="rootPath"/>; null when it names nothing, or is not a pointer (it doesn't start with <c>/</c>).
    /// </summary>
    public static (JsonValue Value, string Path)? Resolve(JsonValue root, string rootPath, string pointer)
    {
        if (pointer.Length == 0)
            return (root, rootPath);
        if (pointer[0] != '/')
            return null;
        var (value, path) = (root, rootPath);
        foreach (var raw in Uri.UnescapeDataString(pointer)[1..].Split('/'))
        {
            var token = raw.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            switch (value)
            {
                case JsonObjectValue obj when obj.Members.LastOrDefault(m => m.Name == token) is { } member:
                    (value, path) = (member.Value, ConfigPath.Child(path, token));
                    break;
                case JsonArrayValue array when int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < array.Items.Count:
                    (value, path) = (array.Items[index], ConfigPath.Index(path, index));
                    break;
                default:
                    return null;
            }
        }

        return (value, path);
    }
}
