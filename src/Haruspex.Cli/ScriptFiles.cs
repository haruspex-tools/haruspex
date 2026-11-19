using System.Security.Cryptography;
using System.Text;
using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Cli;

/// <summary>
/// The script files a document's locations point into, and each one's SHA-256 (<see cref="IrSource.Files"/>): recorded by
/// <c>generate</c>, and checked by <c>explain</c> before it prints a line of one, so that a file edited since shows no line rather
/// than the wrong one. One hash for both, of the file's text with its line endings made LF, in UTF-8, so that a checkout that
/// writes CRLF hashes the same as one that writes LF.
/// </summary>
internal static class ScriptFiles
{
    /// <summary>The SHA-256 of the file at <paramref name="path"/> as this class hashes one, or null when it can't be read.</summary>
    public static string? Sha256(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Each script file <paramref name="document"/>'s locations point into, by its path as a location writes it, with its hash, the
    /// file found in <paramref name="folderOf"/> its path. A location in a package is the package's to match, by its own hash.
    /// </summary>
    public static EquatableMap<string, string> Of(IrDocument document, Func<string, string> folderOf)
    {
        ArgumentNullException.ThrowIfNull(document);
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var at in Locations(document))
        {
            if (!SourceLocation.TryParse(at, out var location) || location.File.Contains(".dacpac/", StringComparison.OrdinalIgnoreCase) || files.ContainsKey(location.File))
                continue;
            if (Sha256(Path.Combine(folderOf(location.File), location.File)) is { } sha256)
                files[location.File] = sha256;
        }

        return files.ToEquatableMap(f => f.Key, f => f.Value);
    }

    /// <summary>Every location the document holds: objects', reasons', unknowns' and source columns'.</summary>
    private static IEnumerable<string> Locations(IrDocument document)
    {
        foreach (var obj in document.Objects)
        {
            yield return obj.At;
            foreach (var result in obj.Results ?? [])
            {
                foreach (var at in Reasons(result.AbsentBecause).Concat(Nodes(result.Type)))
                    yield return at;
            }
        }

        foreach (var field in document.Types.Values.SelectMany(t => t.Fields))
        {
            foreach (var at in Reasons(field.NullableBecause).Concat(field.From.Select(c => c.At)).Concat(Nodes(field.Type)))
                yield return at;
        }

        foreach (var unknown in document.Unknowns)
            yield return unknown.At;

        static IEnumerable<string> Reasons(IEnumerable<IrNullReason> reasons) => reasons.Select(r => r.At);

        static IEnumerable<string> Nodes(IrType type) => IrTypeTree.Within(type).SelectMany(node => node switch
        {
            IrArray array => Reasons(array.ItemsNullableBecause),
            IrTuple tuple => tuple.Items.SelectMany(e => Reasons(e.NullableBecause)),
            IrRecord record => Reasons(record.ValuesNullableBecause),
            _ => [],
        });
    }
}
