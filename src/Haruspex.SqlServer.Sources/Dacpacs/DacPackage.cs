using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Sources.Dacpacs;

/// <summary>
/// One DACPAC, opened: an OPC zip whose <c>model.xml</c> holds the schema and whose <c>Origin.xml</c> says what made it. It is
/// read once, whatever refers to it, and kept only while the model is read.
/// </summary>
/// <param name="Path">Where it was found.</param>
/// <param name="Sha256">The SHA-256 of its bytes, in lower-case hexadecimal, which the IR records so that its claims are reproducible.</param>
/// <param name="Built">
/// Whether a project's build made it (<c>Origin.xml</c> names DacFx's build task), rather than SqlPackage from a database.
/// The two differ in what they keep: a build keeps the project's text of every module, encrypted or not, and doesn't record
/// whether a constraint added by <c>ALTER TABLE … WITH NOCHECK</c> is trusted; an extract keeps no encrypted text and marks
/// each constraint the server doesn't trust (both observed in <c>tests/dacpacs</c>).
/// </param>
/// <param name="Model">The <c>Model</c> element of <c>model.xml</c>, whose children are the schema's elements.</param>
/// <param name="References">The database references its header records, in order.</param>
/// <param name="CompatibilityMode">The compatibility level its header records, or null.</param>
internal sealed class DacPackage(string Path, string Sha256, bool Built, XElement Model, IReadOnlyList<PackageReference> References, int? CompatibilityMode)
{
    /// <summary>The compatibility level the header records, used when the database's options don't give one.</summary>
    public int? CompatibilityMode { get; } = CompatibilityMode;

    /// <summary>Where it was found.</summary>
    public string Path { get; } = Path;

    /// <summary>The SHA-256 of its bytes.</summary>
    public string Sha256 { get; } = Sha256;

    /// <summary>Whether a project's build made it.</summary>
    public bool Built { get; } = Built;

    /// <summary>The <c>Model</c> element of <c>model.xml</c>.</summary>
    public XElement Model { get; } = Model;

    /// <summary>The database references its header records.</summary>
    public IReadOnlyList<PackageReference> References { get; } = References;

    /// <summary>Its file name, which names it in locations and the IR.</summary>
    public string Name => System.IO.Path.GetFileName(Path);

    /// <summary>The package at <paramref name="path"/>, or null when it can't be read as one, with why added to <paramref name="diagnostics"/>.</summary>
    public static DacPackage? Open(string path, ICollection<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        try
        {
            var bytes = File.ReadAllBytes(path);
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var model = Part(zip, "model.xml");
            var origin = Part(zip, "Origin.xml");
            if (model?.Root is not { } root || root.Element(ModelXml.Name("Model")) is not { } elements)
            {
                diagnostics.Add(SourceCodes.UnreadablePackage.At(new FileLocation(path), $"{System.IO.Path.GetFileName(path)} holds no model.xml with a model, so it is not a DACPAC."));
                return null;
            }

            return new DacPackage(
                path,
                Convert.ToHexStringLower(SHA256.HashData(bytes)),
                BuiltByProject(origin),
                elements,
                [.. Header(root)],
                HeaderCompatibility(root));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or XmlException)
        {
            diagnostics.Add(SourceCodes.UnreadablePackage.At(new FileLocation(path), $"{System.IO.Path.GetFileName(path)} can't be read as a DACPAC: {e.Message}"));
            return null;
        }
    }

    /// <summary>A part of the package as XML, or null when it has none; a DTD is refused, as a package never has one.</summary>
    private static XDocument? Part(ZipArchive zip, string name)
    {
        if (zip.GetEntry(name) is not { } entry)
            return null;
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }

    /// <summary>Whether <c>Origin.xml</c> names DacFx's build task as what made the package.</summary>
    private static bool BuiltByProject(XDocument? origin) =>
        origin?.Root?.Element(ModelXml.Name("Operation"))?.Element(ModelXml.Name("ProductName"))?.Value
            .StartsWith("Microsoft.Data.Tools.Schema.Tasks.Sql", StringComparison.Ordinal) == true;

    /// <summary>The database references in the header, each a <c>CustomData</c> of the category <c>Reference</c>.</summary>
    private static IEnumerable<PackageReference> Header(XElement root)
    {
        var header = root.Element(ModelXml.Name("Header"));
        foreach (var data in header?.Elements(ModelXml.Name("CustomData")) ?? [])
        {
            if ((string?)data.Attribute("Category") != ModelXml.Reference)
                continue;
            var metadata = data.Elements(ModelXml.Name("Metadata")).ToDictionary(m => (string?)m.Attribute("Name") ?? "", m => (string?)m.Attribute("Value") ?? "", StringComparer.Ordinal);
            if (!metadata.TryGetValue(ModelXml.FileName, out var file))
                continue;
            var logical = metadata.GetValueOrDefault(ModelXml.LogicalName) is { Length: > 0 } named ? named : FileNameOf(file);
            yield return new PackageReference(file, logical, metadata.GetValueOrDefault(ModelXml.ExternalParts) is { Length: > 0 } parts ? ElementName.Last(parts) : null);
        }
    }

    /// <summary>The compatibility level the header's <c>CompatibilityMode</c> records, or null.</summary>
    private static int? HeaderCompatibility(XElement root) =>
        root.Element(ModelXml.Name("Header"))?.Elements(ModelXml.Name("CustomData"))
            .Where(d => (string?)d.Attribute("Category") == ModelXml.CompatibilityMode)
            .SelectMany(d => d.Elements(ModelXml.Name("Metadata")))
            .Select(m => int.TryParse((string?)m.Attribute("Value"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var level) ? level : (int?)null)
            .FirstOrDefault(level => level is not null);

    /// <summary>A recorded path's file name, whichever machine's separators it has.</summary>
    public static string FileNameOf(string recorded) => recorded[(recorded.LastIndexOfAny(['/', '\\']) + 1)..];
}
