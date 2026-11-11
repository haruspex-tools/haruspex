using Haruspex.Config;
using Haruspex.SqlServer.Model;

namespace Haruspex.Cli;

/// <summary>The outcome of <see cref="SqlProject.Select"/>.</summary>
internal sealed record Selection(IReadOnlyList<ObjectName> Procedures, IReadOnlyList<ObjectPattern> Unmatched);
