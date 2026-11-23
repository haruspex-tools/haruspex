using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// No schema the search looks in has an object of the name in the project, so it means <c>dbo</c>'s, the last schema SQL Server
/// looks in, which the project doesn't have.
/// </summary>
/// <param name="InDbo">The name it then means: in <c>dbo</c>.</param>
public sealed record NoObject(ObjectName InDbo) : SchemaNameResolution;
