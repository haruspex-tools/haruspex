using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.Tests.Sources;

/// <summary>
/// For the proof that a DACPAC reads as its project's folder does: each location in a folder's output, rewritten as the same
/// place in the package, so the two outputs can be compared byte for byte. It is built from names and texts alone, never from
/// the reader's own positions, which would make it agree with any reader:
/// <list type="bullet">
/// <item>a module's place is found by finding its text, as <c>model.xml</c> holds it (header and body), in the folder's file, and
/// a location in it keeps its line and column relative to where that text starts;</item>
/// <item>a column's place is its position among the table's columns in <c>model.xml</c>, and a constraint's or index's its position
/// among the table's parts after them (named by its name, or an unnamed check by its expression), as
/// <c>docs/decisions/0039-a-location-in-a-dacpac-names-the-package-and-the-object.md</c> defines a
/// table's locations; a location inside a check's or computed column's expression keeps its offset from the expression's
/// start.</item>
/// </list>
/// A location the map can't place stays as it was and is listed, so the comparison fails on it. A database read from several
/// packages (one and the packages of its database it refers to) is mapped package by package: each object to the package that
/// holds it.
/// </summary>
internal sealed partial class PackageLocationMap
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02";

    private static readonly HashSet<string> Parts = new(StringComparer.Ordinal)
    {
        "SqlCheckConstraint", "SqlPrimaryKeyConstraint", "SqlUniqueConstraint", "SqlIndex", "SqlForeignKeyConstraint",
    };

    private readonly List<(string File, Position Start, Position End, string Target, Position Origin)> regions = [];
    private readonly Dictionary<SourceLocation, SourceLocation> points = [];

    /// <summary>The folder's files that hold something placed, longest first, so that a path inside another isn't matched first.</summary>
    private readonly List<string> files;

    /// <summary>The locations a <see cref="Map"/> could not place, each once.</summary>
    public SortedSet<string> Unmapped { get; } = new(StringComparer.Ordinal);

    public PackageLocationMap(DatabaseModel folder, string folderRoot, params string[] packagePaths)
    {
        foreach (var packagePath in packagePaths)
        {
            var package = Path.GetFileName(packagePath);
            using var zip = ZipFile.OpenRead(packagePath);
            using var stream = zip.GetEntry("model.xml")!.Open();
            var elements = XDocument.Load(stream).Root!.Element(Ns + "Model")!.Elements(Ns + "Element").ToList();
            foreach (var module in folder.Procedures.Cast<SchemaObject>().Concat(folder.Functions).Concat(folder.Views).Concat(folder.Triggers))
                AddModule(module, elements, folderRoot, package);
            foreach (var table in folder.Tables)
                AddTable(table, elements, package);
            foreach (var type in folder.UserTypes.OfType<TableType>())
                AddTableType(type, elements, package);
        }

        files = [.. regions.Select(r => r.File).Concat(points.Keys.Select(p => p.File)).Distinct(StringComparer.Ordinal).OrderByDescending(f => f.Length)];
    }

    /// <summary>Every <c>file:line:column</c> of a file the folder's model holds something of, wherever it is in the text.</summary>
    private Regex Locations() =>
        new($"(?<file>{string.Join('|', files.Select(Regex.Escape))}):(?<line>[0-9]+):(?<column>[0-9]+)", RegexOptions.CultureInvariant);

    /// <summary>The text with each <c>path:line:column</c> of a folder rewritten as the package's.</summary>
    public string Map(string text) => Locations().Replace(text, match =>
    {
        var at = new SourceLocation(match.Groups["file"].Value, int.Parse(match.Groups["line"].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups["column"].Value, CultureInfo.InvariantCulture));
        if (points.TryGetValue(at, out var point))
            return point.ToString();
        var position = new Position(at.Line, at.Column);
        foreach (var (file, start, end, target, origin) in regions)
        {
            if (file == at.File && start.CompareTo(position) <= 0 && position.CompareTo(end) <= 0)
                return Shifted(target, origin, start, position).ToString();
        }

        Unmapped.Add(match.Value);
        return match.Value;
    });

    private void AddModule(SchemaObject module, List<XElement> elements, string folderRoot, string package)
    {
        var element = elements.FirstOrDefault(e => Name(e) is { } name && SameObject(name, module.Name) && e.Attribute("Type")!.Value is not "SqlTable" and not "SqlSchema" and not "SqlSynonym");
        if (element is null)
            return;
        var textElement = element.Element(Ns + "Relationship") is not null && Relationship(element, "FunctionBody").FirstOrDefault() is { } body ? body : element;
        var header = textElement.Elements(Ns + "Annotation").FirstOrDefault(a => (string?)a.Attribute("Type") == "SysCommentsObjectAnnotation") is { } comments ? Property(comments, "HeaderContents") : null;
        var script = Property(textElement, "BodyScript") ?? Property(element, "QueryScript");
        if (header is null || script is null)
            return;
        // A header keeps its carriage returns (as character references) and a body's CDATA loses them to XML's line-end
        // handling, so both sides are compared with every line end one LF; a line is a line either way.
        var file = Normalised(File.ReadAllText(Path.Combine(folderRoot, module.Span.File), Encoding.UTF8));
        var definition = Normalised(header + script);
        var index = file.IndexOf(definition, StringComparison.Ordinal);
        if (index < 0)
            return;
        regions.Add((module.Span.File, PositionOf(file, index), PositionOf(file, index + definition.Length), PackageFile(package, module.Name), new Position(1, 1)));
    }

    private void AddTable(Table table, List<XElement> elements, string package)
    {
        if (elements.FirstOrDefault(e => (string?)e.Attribute("Type") == "SqlTable" && SameObject(Name(e)!, table.Name)) is not { } element)
            return;
        var parts = elements.Where(e => Parts.Contains((string?)e.Attribute("Type") ?? "") && Owner(e) is { } owner && SameObject(owner, table.Name)).ToList();
        AddParts(table, table.Name, element, parts, "", package);
    }

    /// <summary>A table type, whose constraints are entries of its own element (<c>Constraints</c>), defaults among them, in that order.</summary>
    private void AddTableType(TableType type, List<XElement> elements, string package)
    {
        if (elements.FirstOrDefault(e => (string?)e.Attribute("Type") == "SqlTableType" && SameObject(Name(e)!, type.Name)) is not { } element)
            return;
        AddParts(type, type.Name, element, [.. Relationship(element, "Constraints")], "SqlTableType", package);
    }

    /// <summary>
    /// A table's or table type's places: its columns by their place among its columns, and its constraints by their place among
    /// its parts after them, each part's type <paramref name="prefix"/> followed by the table's (<c>SqlTableTypeCheckConstraint</c>).
    /// </summary>
    private void AddParts(ITable table, ObjectName name, XElement element, List<XElement> parts, string prefix, string package)
    {
        string Part(string type) => prefix.Length == 0 ? type : prefix + type["Sql".Length..];
        var target = PackageFile(package, name);
        var columns = Relationship(element, "Columns").Select(c => Last(Name(c)!)).ToList();
        points[table.Span.Start] = new SourceLocation(target, 1, 1);
        foreach (var column in table.Columns)
        {
            var line = columns.FindIndex(c => c.Equals(column.Name, StringComparison.OrdinalIgnoreCase)) + 1;
            points[column.At] = new SourceLocation(target, line, 1);
            if (column.ComputedExpression is { } computed)
                regions.Add((column.At.File, Start(computed), End(computed), target, new Position(line, 1)));
        }

        foreach (var check in table.Checks)
        {
            var text = Words(FragmentText(check.Expression));
            var part = parts.FindIndex(p => (string?)p.Attribute("Type") == Part("SqlCheckConstraint")
                && (check.Name is { } name ? Name(p) is { } partName && Last(partName).Equals(name, StringComparison.OrdinalIgnoreCase) : Name(p) is null && Words(Property(p, "CheckExpressionScript") ?? "") == text));
            if (part < 0)
                continue;
            var line = columns.Count + part + 1;
            points[check.At] = new SourceLocation(target, line, 1);
            regions.Add((check.At.File, Start(check.Expression), End(check.Expression), target, new Position(line, 1)));
        }

        foreach (var key in table.UniqueKeys)
        {
            var part = parts.FindIndex(p => new[] { Part("SqlPrimaryKeyConstraint"), Part("SqlUniqueConstraint"), Part("SqlIndex") }.Contains((string?)p.Attribute("Type"))
                && (key.Name is { } name ? Name(p) is { } partName && Last(partName).Equals(name, StringComparison.OrdinalIgnoreCase) : Name(p) is null && KeyColumns(p).SequenceEqual(key.Columns, StringComparer.OrdinalIgnoreCase)));
            if (part >= 0)
                points[key.At] = new SourceLocation(target, columns.Count + part + 1, 1);
        }
    }

    /// <summary>A location in a region: its line counted from the region's start, and its column too on that first line.</summary>
    private static SourceLocation Shifted(string target, Position origin, Position start, Position at) =>
        at.Line == start.Line
            ? new SourceLocation(target, origin.Line, origin.Column + at.Column - start.Column)
            : new SourceLocation(target, origin.Line + at.Line - start.Line, at.Column);

    private static string PackageFile(string package, ObjectName name) => $"{package}/[{name.Schema.Replace("]", "]]", StringComparison.Ordinal)}].[{name.Name.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static Position Start(TSqlFragment fragment) => new(fragment.StartLine, fragment.StartColumn);

    private static Position End(TSqlFragment fragment)
    {
        var last = fragment.ScriptTokenStream[fragment.LastTokenIndex];
        return new Position(last.Line, last.Column + last.Text.Length);
    }

    private static string FragmentText(TSqlFragment fragment) =>
        string.Concat(Enumerable.Range(fragment.FirstTokenIndex, fragment.LastTokenIndex - fragment.FirstTokenIndex + 1).Select(i => fragment.ScriptTokenStream[i].Text));

    private static string Words(string text) => WhiteSpace().Replace(text, " ").Trim();

    private static string Normalised(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static Position PositionOf(string text, int index)
    {
        var lineStart = index == 0 ? 0 : text.LastIndexOf('\n', index - 1) + 1;
        return new Position(1 + text.AsSpan(0, index).Count('\n'), index - lineStart + 1);
    }

    private static IEnumerable<XElement> Relationship(XElement element, string name) =>
        element.Elements(Ns + "Relationship").Where(r => (string?)r.Attribute("Name") == name).Elements(Ns + "Entry").Elements(Ns + "Element");

    private static IEnumerable<string> KeyColumns(XElement part) =>
        Relationship(part, "ColumnSpecifications").SelectMany(s => s.Descendants(Ns + "References")).Select(r => Last((string)r.Attribute("Name")!));

    private static string? Owner(XElement part) =>
        part.Elements(Ns + "Relationship").Where(r => (string?)r.Attribute("Name") is "DefiningTable" or "IndexedObject")
            .Elements(Ns + "Entry").Elements(Ns + "References").Select(r => (string?)r.Attribute("Name")).FirstOrDefault();

    private static string? Name(XElement element) => (string?)element.Attribute("Name");

    private static string? Property(XElement element, string name) =>
        element.Elements(Ns + "Property").FirstOrDefault(p => (string?)p.Attribute("Name") == name) is { } p
            ? (string?)p.Attribute("Value") ?? p.Element(Ns + "Value")?.Value
            : null;

    private static string[] PartsOf(string name) => [.. BracketPart().Matches(name).Select(m => m.Groups[1].Value.Replace("]]", "]", StringComparison.Ordinal))];

    private static string Last(string name) => PartsOf(name)[^1];

    private static bool SameObject(string name, ObjectName objectName) => PartsOf(name) is [var schema, var obj] && new ObjectName(schema, obj) == objectName;

    [GeneratedRegex(@"\[((?:[^\]]|\]\])*)\]")]
    private static partial Regex BracketPart();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    private readonly record struct Position(int Line, int Column) : IComparable<Position>
    {
        public int CompareTo(Position other) => Line != other.Line ? Line.CompareTo(other.Line) : Column.CompareTo(other.Column);
    }
}
