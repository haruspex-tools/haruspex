using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// A pre- or post-deployment script haruspex can't read in full: one that doesn't parse (another sqlcmd command, a variable no
/// one defines), or a file it includes that can't be found or read. What it does to an object it names isn't known, so every
/// table, procedure, function and view whose name it holds is doubted. A keyword search would miss an <c>ALTER</c>, so the
/// names are every identifier in its tokens, which claims less than a search for what it does would.
/// </summary>
/// <param name="identifiers">The names it holds, ignoring case; null when not even those are known (an include not found), so that it names every object.</param>
/// <param name="at">Where it is.</param>
internal sealed class UnreadDeployment(IReadOnlySet<string>? identifiers, SourceLocation at)
{
    /// <summary>The names it holds, ignoring case; null for every name.</summary>
    public IReadOnlySet<string>? Identifiers { get; } = identifiers;

    /// <summary>Where it is.</summary>
    public SourceLocation At { get; } = at;

    /// <summary>Whether it may name <paramref name="name"/>: holds its name, without its schema, as an identifier.</summary>
    public bool Names(ObjectName name) => Identifiers is null || Identifiers.Contains(name.Name);
}
