using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources.Dacpacs;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>What <see cref="SqlProjectReader"/> found: the database's model, the projects it was read from, and the problems.</summary>
/// <param name="Model">The schema of the project and the projects and packages of its database it refers to, holding the other databases it refers to. When there are errors it is incomplete.</param>
/// <param name="Projects">Every project read, the one given first, as full paths.</param>
/// <param name="References">
/// Every DACPAC the projects refer to, directly or through another package, each once, in the order met: found and hashed, or not
/// found. A project of another database is read from its scripts, as the project given is, and isn't listed.
/// </param>
/// <param name="Diagnostics">Problems found while reading, each once, in the order found. Locations are relative to the given project's folder.</param>
public sealed record SqlProjectReadResult(DatabaseModel Model, EquatableArray<string> Projects, EquatableArray<ReferenceRead> References, EquatableArray<Diagnostic> Diagnostics);
