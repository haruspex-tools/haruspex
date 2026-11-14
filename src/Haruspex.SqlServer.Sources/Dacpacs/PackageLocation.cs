using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources.Dacpacs;

/// <summary>
/// Where something read from a DACPAC is, as a location: the package and the object as the file
/// (<c>sales.dacpac/[sales].[get_order]</c>), and a line and column in the object. A package keeps no file names, so this is
/// the most a location can say, and it reads as a path into the package
/// (<c>docs/decisions/0039-a-location-in-a-dacpac-names-the-package-and-the-object.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>In a module</b> (a procedure, function, view or trigger), the line and column are in the module's own text, its first
/// line 1: the text <c>sys.sql_modules</c> holds, which is all a package extracted from a database has, and which a built
/// package has too (its <c>StartLine</c>, a line in the project's file, is not used, so that a location means the same however
/// the package was made).
/// </para>
/// <para>
/// <b>In a table</b>, which <c>model.xml</c> holds as elements with no text, the line is the part's place in the table's list:
/// its columns in order, then the constraints and indexes on it in the order <c>model.xml</c> gives them; the column is 1. It is
/// a position in that list, not in any file, and it keeps two unnamed constraints apart.
/// </para>
/// <para>The object's name is always bracketed, so a name holding <c>:</c>, <c>/</c> or <c>.</c> can't be misread.</para>
/// </remarks>
internal static class PackageLocation
{
    /// <summary>The file part of a location in <paramref name="name"/>, an object of <paramref name="package"/>.</summary>
    public static string File(string package, ObjectName name) => package + "/" + ElementName.Quoted(name);

    /// <summary>A span that starts at the object's first line, for an object that has no text of its own to measure.</summary>
    public static SourceSpan Whole(string file, int lines) => new(file, 1, 1, Math.Max(lines, 1), 1);
}
