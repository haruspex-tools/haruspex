using System.Xml;
using System.Xml.Linq;
using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// One MSBuild file, loaded: a project file or a file it imports, with where it is and the name it is shown by in locations.
/// Elements keep their line and column, so a doubt or a problem points to the element it is about.
/// </summary>
/// <param name="Path">The file's full path.</param>
/// <param name="Shown">How locations name it: its path relative to the folder of the project read first, with <c>/</c>.</param>
/// <param name="Root">Its root element, <c>Project</c>.</param>
internal sealed class ProjectFile(string Path, string Shown, XElement Root)
{
    /// <summary>The file's full path.</summary>
    public string Path { get; } = Path;

    /// <summary>How locations name it.</summary>
    public string Shown { get; } = Shown;

    /// <summary>Its root element.</summary>
    public XElement Root { get; } = Root;

    /// <summary>The folder it is in, which <c>$(MSBuildThisFileDirectory)</c> names and its imports are relative to.</summary>
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? ".";

    /// <summary>Where <paramref name="node"/> is in it.</summary>
    public SourceLocation At(XObject node) =>
        node is IXmlLineInfo info && info.HasLineInfo() ? new SourceLocation(Shown, info.LineNumber, info.LinePosition) : new SourceLocation(Shown, 1, 1);

    /// <summary>
    /// The file at <paramref name="path"/>, or null with why when it can't be read as MSBuild: it is missing, isn't XML (a DTD
    /// is refused, as MSBuild refuses one), or its root isn't <c>Project</c>.
    /// </summary>
    public static ProjectFile? Load(string path, string shown, out string? problem)
    {
        problem = null;
        try
        {
            using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var document = XDocument.Load(reader, LoadOptions.SetLineInfo);
            if (document.Root is { Name.LocalName: ProjectXml.Project } root)
                return new ProjectFile(path, shown, root);
            problem = "its root element isn't Project, so it isn't an MSBuild file";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException)
        {
            problem = e.Message;
        }

        return null;
    }
}
