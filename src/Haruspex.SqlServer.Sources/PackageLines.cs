using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using Haruspex.SqlServer.Sources.Dacpacs;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// A line of a module in a DACPAC, for a person reading a location in it: what <c>haruspex explain</c> prints under a reference
/// to a package (<c>sales.dacpac/[sales].[get_order]:3:5</c>), whose line is in the module's own text
/// (<c>docs/decisions/0039-a-location-in-a-dacpac-names-the-package-and-the-object.md</c>).
/// </summary>
/// <remarks>
/// A package keeps no file names: a built one records where each module's text started in its project's file, but not which file
/// (observed in <c>tests/dacpacs</c>), so the module's own text is what a location can be shown in.
/// </remarks>
public static class PackageLines
{
    /// <summary>
    /// Line <paramref name="line"/> of the module <paramref name="objectName"/> (bracketed, as a location writes it: <c>[sales].[get_order]</c>)
    /// in the package at <paramref name="path"/>, when the package is there and its SHA-256 is <paramref name="sha256"/>, so the
    /// line is the one the location was written from. Null otherwise, or when the object is a table, which has no text, or the
    /// package can't be read: a reader of the location then has the reference alone.
    /// </summary>
    public static string? Line(string path, string sha256, string objectName, int line)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(sha256);
        try
        {
            if (!File.Exists(path))
                return null;
            var bytes = File.ReadAllBytes(path);
            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), sha256, StringComparison.OrdinalIgnoreCase))
                return null;
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            if (zip.GetEntry("model.xml") is not { } entry)
                return null;
            using var stream = entry.Open();
            var text = XDocument.Load(stream).Descendants(ModelXml.Namespace + "Element")
                .Where(e => string.Equals((string?)e.Attribute("Name"), objectName, StringComparison.Ordinal))
                .Select(ModuleTextReader.Text)
                .FirstOrDefault(t => t is not null);
            var lines = text is null ? null : SourceLines.Of(text);
            return lines is not null && line >= 1 && line <= lines.Count ? lines[line - 1] : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or XmlException)
        {
            return null;
        }
    }
}
