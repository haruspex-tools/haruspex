using System.Security.Cryptography;
using Haruspex.Diagnostics;

namespace Haruspex.Config;

/// <summary>
/// The schema files a config file's declarations read, each read and parsed once, with the SHA-256 of its bytes: a column's
/// declaration records the hash of every file it read, so that a change to one is a change to the IR
/// (docs/decisions/0093-a-column-s-json-is-part-of-its-type-completed-by-the-config.md).
/// </summary>
/// <param name="directory">The config file's folder, which the IR writes a file's path relative to.</param>
/// <param name="problems">Where a file that can't be read or parsed is reported, once.</param>
internal sealed class SchemaFiles(string directory, List<Diagnostic> problems)
{
    private readonly Dictionary<string, (SchemaDocument? Document, string? Sha256)> read = new(StringComparer.Ordinal);

    /// <summary>
    /// The file at <paramref name="fullPath"/>, read and parsed, or null when it can't be (reported once, at
    /// <paramref name="location"/>, the place that named it first).
    /// </summary>
    public SchemaDocument? Load(string fullPath, JsonLocation location, string named)
    {
        if (read.TryGetValue(fullPath, out var known))
            return known.Document;
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(fullPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problems.Add(ConfigCodes.Unreadable.At(location, $"cannot read the schema {named}: {e.Message}"));
            read[fullPath] = (null, null);
            return null;
        }

        var parsed = ConfigJson.Parse(bytes, fullPath);
        problems.AddRange(parsed.Diagnostics);
        var document = parsed is Succeeded<JsonValue> { Value: var root } ? new SchemaDocument(fullPath, root, "") : null;
        read[fullPath] = (document, Convert.ToHexStringLower(SHA256.HashData(Text(bytes))));
        return document;
    }

    /// <summary>
    /// A file's bytes as the hash reads them: without a UTF-8 byte order mark, and with each CRLF and each lone CR as LF. The same
    /// schema checked out on Windows, where git may write CRLF, and elsewhere then gives the same hash, so the IR is the same.
    /// </summary>
    private static byte[] Text(byte[] bytes)
    {
        var start = bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;
        var text = new List<byte>(bytes.Length);
        for (var i = start; i < bytes.Length; i++)
        {
            if (bytes[i] != (byte)'\r')
                text.Add(bytes[i]);
            else if (i + 1 >= bytes.Length || bytes[i + 1] != (byte)'\n')
                text.Add((byte)'\n');
        }

        return [.. text];
    }

    /// <summary>The SHA-256 of a file read, of its text with LF line endings and no byte order mark, as 64 lower-case hexadecimal digits.</summary>
    public string Sha256(string fullPath) => read[fullPath].Sha256 ?? throw new InvalidOperationException($"{fullPath} was not read.");

    /// <summary>A file's path as the IR writes it: relative to the config file's folder, with <c>/</c> between folders.</summary>
    public string Relative(string fullPath) => Path.GetRelativePath(directory, fullPath).Replace('\\', '/');
}
