using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>A target with something in it, which runs during a build and isn't read: reported once by its name, however many projects define it.</summary>
/// <param name="Name">Its name.</param>
/// <param name="At">Where it is defined.</param>
internal sealed record ProjectTarget(string Name, SourceLocation At);
