using Haruspex.SqlServer.Model.XmlSchemas;

namespace Haruspex.SqlServer.Semantics.XmlSchemas;

/// <summary>A step of an <see cref="XmlPath"/>.</summary>
/// <param name="Name">The node's name, its namespace bound.</param>
/// <param name="IsAttribute">Whether it names an attribute rather than a child element.</param>
/// <param name="First">Whether it is followed by <c>[1]</c>, which keeps only the first such node of each parent.</param>
public sealed record XmlPathStep(XmlName Name, bool IsAttribute, bool First);
