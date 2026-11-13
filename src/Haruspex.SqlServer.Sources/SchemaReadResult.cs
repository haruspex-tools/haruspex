using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources;

/// <summary>What <see cref="SqlScriptFolder"/> found in a project's scripts.</summary>
/// <param name="Model">The schema. When there are diagnostics it is incomplete: a file that does not parse adds nothing.</param>
/// <param name="Diagnostics">Errors found while reading, such as parse errors and duplicate definitions.</param>
public sealed record SchemaReadResult(DatabaseModel Model, EquatableArray<Diagnostic> Diagnostics);
