namespace Haruspex.SqlServer.Semantics.XmlSchemas;

/// <summary>Where a path starts: an instance's top level (<see cref="XmlInstanceStart"/>), or a node that <c>nodes()</c> gave (<see cref="XmlNodeStart"/>).</summary>
/// <remarks>A closed hierarchy (docs/decisions/0031-the-model-is-closed-hierarchies-of-records.md).</remarks>
public abstract record XmlStart
{
    /// <summary>Creates a start; only the kinds in this assembly can.</summary>
    private protected XmlStart()
    {
    }
}
