using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>No schema of the project has an object of the name, so it means <c>dbo</c>'s, which the project doesn't have.</summary>
/// <param name="InDbo">The name it then means: in <c>dbo</c>.</param>
public sealed record NoObject(ObjectName InDbo) : SchemaNameResolution;
