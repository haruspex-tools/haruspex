using Haruspex.SqlServer.Model;

namespace Haruspex.TestSupport;

/// <summary>The procedures a test selected (<see cref="SqlProjectTestExtensions.Select"/>).</summary>
internal sealed record TestSelection(IReadOnlyList<ObjectName> Procedures);
