using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// What one script leaves of an object it names (<see cref="ScriptDefinitions{T}"/>): a definition it made, one it only altered
/// from what stood before it, or none.
/// </summary>
/// <typeparam name="T">A procedure, a function, or a view's definition.</typeparam>
/// <param name="Name">The object.</param>
/// <param name="Standing">The definition that stands once the script has run, or null when it leaves none.</param>
/// <param name="At">Where <paramref name="Standing"/> is; null with it.</param>
/// <param name="Established">Whether the script made the object itself, rather than only altering what stood before it.</param>
/// <param name="Why">
/// For a definition the script made, the statement that leaves it in doubt, or null; for one it only altered, its first
/// <c>ALTER</c>; for none, its first drop.
/// </param>
internal sealed record ScriptOutcome<T>(ObjectName Name, T? Standing, SourceLocation? At, bool Established, Redefinition? Why)
    where T : class;
