using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>What <see cref="SqlProjectReader"/> found: the database's model, the projects it was read from, and the problems.</summary>
/// <param name="Model">The schema of the project and the projects of the same database it refers to. When there are errors it is incomplete.</param>
/// <param name="Projects">Every project read, the one given first, as full paths.</param>
/// <param name="Diagnostics">Problems found while reading, each once, in the order found. Locations are relative to the given project's folder.</param>
public sealed record SqlProjectReadResult(DatabaseModel Model, EquatableArray<string> Projects, EquatableArray<Diagnostic> Diagnostics);
