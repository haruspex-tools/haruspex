namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// Not a function: XML's <c>x.nodes('path') AS t(c)</c>, which the parser reads as a call of a function named by the parts before
/// it, and which a column list tells apart, since a function's is refused (317). Why:
/// docs/decisions/0133-an-xml-value-is-a-kind-of-its-own-a-branded-string.md.
/// </summary>
public sealed record XmlNodesCallee : Callee;
