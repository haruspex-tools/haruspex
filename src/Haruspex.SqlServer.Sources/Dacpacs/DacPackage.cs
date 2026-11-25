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
/// <param name="Origin">
/// What made it (<see cref="OriginOf"/>). A build and an extract differ in what they keep: a build keeps the project's text of
/// every module, encrypted or not, and doesn't record whether a constraint added by <c>ALTER TABLE … WITH NOCHECK</c> is trusted;
/// an extract keeps no encrypted text, marks each constraint the server doesn't trust, and marks a disabled index, but writes
/// nothing on a primary key or unique constraint whose index is disabled (observed, decision 0120), which a build can't hold
/// (it refuses <c>ALTER INDEX</c> on a constraint's index, SQL71001). So a check that isn't marked is trusted only in an
/// extract, and a key constraint only in a build; a package that is neither, as far as it says, trusts neither.
/// </param>
/// <param name="Model">The <c>Model</c> element of <c>model.xml</c>, whose children are the schema's elements.</param>
/// <param name="References">The database references its header records, in order.</param>
/// <param name="CompatibilityMode">The compatibility level its header records, or null.</param>
/// <param name="Deployment">
/// Its pre- and post-deployment scripts (<c>predeploy.sql</c>, <c>postdeploy.sql</c>), each by its part's name with its text, in
/// that order: a build writes each with the files it includes by <c>:r</c> already in its text.
/// </param>
internal sealed class DacPackage(string Path, string Sha256, PackageOrigin Origin, XElement Model, IReadOnlyList<PackageReference> References, int? CompatibilityMode, IReadOnlyList<(string Part, string Text)> Deployment)
{
    /// <summary>The parts of a package that hold its deployment scripts, in the order a deployment runs them.</summary>
    private static readonly string[] DeploymentParts = ["predeploy.sql", "postdeploy.sql"];

    /// <summary>Its pre- and post-deployment scripts, each by its part's name with its text.</summary>
    public IReadOnlyList<(string Part, string Text)> Deployment { get; } = Deployment;

    /// <summary>
    /// The compatibility level the header records, or else the version of the provider the model names
    /// (<c>Sql160DatabaseSchemaProvider</c> is 160): used when the database's options don't give one, as an extract's don't when
    /// the level is its provider's.
    /// </summary>
    public int? CompatibilityMode { get; } = CompatibilityMode;

    /// <summary>Where it was found.</summary>
    public string Path { get; } = Path;

    /// <summary>The SHA-256 of its bytes.</summary>
    public string Sha256 { get; } = Sha256;

    /// <summary>What made it, as far as its <c>Origin.xml</c> says.</summary>
    public PackageOrigin Origin { get; } = Origin;

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
                OriginOf(origin),
                elements,
                [.. Header(root)],
                HeaderCompatibility(root) ?? ProviderLevel(root),
                [.. DeploymentScripts(zip)]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or XmlException)
        {
            diagnostics.Add(SourceCodes.UnreadablePackage.At(new FileLocation(path), $"{System.IO.Path.GetFileName(path)} can't be read as a DACPAC: {e.Message}"));
            return null;
        }
    }

    /// <summary>
    /// The tool that builds a package from a project's scripts, by the assembly <c>Origin.xml</c>'s <c>ProductName</c> names: the
    /// build task Microsoft.Build.Sql and SSDT's legacy projects run (observed in <c>tests/dacpacs</c>). An extract names
    /// <c>Microsoft.SqlServer.Dac</c>. DacFx's API (<c>DacPackageExtensions.BuildPackage</c>, which community SDKs build with)
    /// names <c>Microsoft.SqlServer.Dac.Extensions</c> and no server whatever its model came from, an extract loaded and written
    /// again included (observed, decision 0120), so it isn't taken for a build.
    /// </summary>
    private const string Builder = "Microsoft.Data.Tools.Schema.Tasks.Sql";

    /// <summary>
    /// What made a package: extracted when its <c>Origin.xml</c> names the server (a <c>Server</c> element, which every extract
    /// observed writes and no build does), built when it names no server and its <c>ProductName</c> is the build task, and
    /// otherwise not known.
    /// </summary>
    private static PackageOrigin OriginOf(XDocument? origin)
    {
        if (origin?.Root is not { } root)
            return PackageOrigin.Unknown;
        if (root.Element(ModelXml.Name("Server")) is not null)
            return PackageOrigin.Extracted;
        var product = (string?)root.Element(ModelXml.Name("Operation"))?.Element(ModelXml.Name("ProductName"));
        return product?.Split(',')[0].Trim() == Builder ? PackageOrigin.Built : PackageOrigin.Unknown;
    }

    /// <summary>The deployment scripts the package holds, each as text without its byte order mark.</summary>
    private static IEnumerable<(string Part, string Text)> DeploymentScripts(ZipArchive zip)
    {
        foreach (var part in DeploymentParts)
        {
            if (zip.GetEntry(part) is not { } entry)
                continue;
            using var reader = new StreamReader(entry.Open(), System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            yield return (part, reader.ReadToEnd());
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

    /// <summary>
    /// The database references in the header, each a <c>CustomData</c> of the category <c>Reference</c> and the type
    /// <c>SqlSchema</c>. A reference of another type is not to a database: <c>Microsoft.Build.Sql</c> 1.0.0 lists the project's own
    /// CLR assembly as a <c>Reference</c> of the type <c>Assembly</c> (observed in <c>tests/dacpacs/versions</c>).
    /// </summary>
    private static IEnumerable<PackageReference> Header(XElement root)
    {
        var header = root.Element(ModelXml.Name("Header"));
        foreach (var data in header?.Elements(ModelXml.Name("CustomData")) ?? [])
        {
            if ((string?)data.Attribute("Category") != ModelXml.Reference || (string?)data.Attribute("Type") != ModelXml.SqlSchema)
                continue;
            var metadata = data.Elements(ModelXml.Name("Metadata")).ToDictionary(m => (string?)m.Attribute("Name") ?? "", m => (string?)m.Attribute("Value") ?? "", StringComparer.Ordinal);
            if (!metadata.TryGetValue(ModelXml.FileName, out var file))
                continue;
            var logical = metadata.GetValueOrDefault(ModelXml.LogicalName) is { Length: > 0 } named ? named : FileNameOf(file);
            var parts = metadata.GetValueOrDefault(ModelXml.ExternalParts) is { Length: > 0 } written ? ElementName.Parts(written) : [];
            yield return new PackageReference(file, logical, parts.Count > 0 ? parts[^1] : null, parts.Count > 1 ? parts[^2] : null);
        }
    }

    /// <summary>The compatibility level the header's <c>CompatibilityMode</c> records, or null.</summary>
    private static int? HeaderCompatibility(XElement root) =>
        root.Element(ModelXml.Name("Header"))?.Elements(ModelXml.Name("CustomData"))
            .Where(d => (string?)d.Attribute("Category") == ModelXml.CompatibilityMode)
            .SelectMany(d => d.Elements(ModelXml.Name("Metadata")))
            .Select(m => int.TryParse((string?)m.Attribute("Value"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var level) ? level : (int?)null)
            .FirstOrDefault(level => level is not null);

    /// <summary>The version a provider's name gives, such as 170 for <c>Microsoft.Data.Tools.Schema.Sql.Sql170DatabaseSchemaProvider</c>; null for one with none.</summary>
    private static int? ProviderLevel(XElement root) =>
        System.Text.RegularExpressions.Regex.Match((string?)root.Attribute("DspName") ?? "", @"\.Sql(\d+)DatabaseSchemaProvider$") is { Success: true } match
            ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
            : null;

    /// <summary>A recorded path's file name, whichever machine's separators it has.</summary>
    public static string FileNameOf(string recorded) => recorded[(recorded.LastIndexOfAny(['/', '\\']) + 1)..];
}
