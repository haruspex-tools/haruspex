using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>The source reads a table of the schema.</summary>
/// <param name="Table">The table.</param>
public sealed record TableTarget(Table Table) : SourceTarget;
