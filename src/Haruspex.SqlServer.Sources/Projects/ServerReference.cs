using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// A reference a project makes to a database on another server, which isn't read (<see cref="SourceCodes.OtherServer"/>), kept to
/// be said once for every project that makes it: one in a shared <c>.props</c> is made by many projects of one application.
/// </summary>
/// <param name="Project">The project that makes it, by its file name.</param>
/// <param name="What">What it refers to and why that isn't read, the rest of the sentence after "refers to".</param>
/// <param name="At">Where the reference is.</param>
internal sealed record ServerReference(string Project, string What, SourceLocation At);
