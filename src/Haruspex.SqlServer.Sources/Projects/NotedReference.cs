using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// A reference a project makes that isn't read yet (HAR1011), kept to be said once for every project that makes the same one: a
/// shared <c>.props</c> or SSDT's own <c>master.dacpac</c> is referred to by many projects of one application.
/// </summary>
/// <param name="Project">The project that makes it, by its file name.</param>
/// <param name="What">What it refers to and why that isn't read, the rest of the sentence after "refers to".</param>
/// <param name="At">Where the reference is.</param>
internal sealed record NotedReference(string Project, string What, SourceLocation At);
