using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources.Dacpacs;

/// <summary>
/// What a package defines that an extended property's host may belong to, by element type: a property's name says only the
/// owner's name (<see cref="PackageProperties"/>).
/// </summary>
/// <remarks>A class, not a record: it holds sets, which compare by reference, and nothing compares it.</remarks>
/// <param name="tables">Its tables (<c>SqlTable</c>).</param>
/// <param name="views">Its views.</param>
/// <param name="tableFunctions">Its inline and multi-statement table functions.</param>
/// <param name="functions">Every function it defines, a table function's included.</param>
/// <param name="procedures">Its procedures.</param>
internal sealed class PackageObjects(IReadOnlySet<ObjectName> tables, IReadOnlySet<ObjectName> views, IReadOnlySet<ObjectName> tableFunctions, IReadOnlySet<ObjectName> functions, IReadOnlySet<ObjectName> procedures)
{
    /// <summary>The level type of a column's or an index's owner, or <see cref="PackageProperties.UnsettledOwner"/> when the package doesn't define it.</summary>
    public string ColumnOwner(ObjectName owner) =>
        tables.Contains(owner) ? "TABLE"
        : views.Contains(owner) ? "VIEW"
        : tableFunctions.Contains(owner) ? "FUNCTION"
        : PackageProperties.UnsettledOwner;

    /// <summary>The level type of a parameter's owner, or <see cref="PackageProperties.UnsettledOwner"/> when the package doesn't define it.</summary>
    public string ParameterOwner(ObjectName owner) =>
        procedures.Contains(owner) ? "PROCEDURE"
        : functions.Contains(owner) ? "FUNCTION"
        : PackageProperties.UnsettledOwner;
}
