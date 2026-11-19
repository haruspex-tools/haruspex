using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// A script's change to a procedure or function it doesn't define itself, or the doubt it leaves on one it does
/// (<see cref="ScriptOutcome{T}"/>): its first statement that changes it, and, when the script leaves an <c>ALTER</c>'s
/// definition standing, that definition.
/// </summary>
/// <typeparam name="T">The kind of module.</typeparam>
/// <param name="Why">The statement.</param>
/// <param name="Altered">The definition an <c>ALTER</c> leaves standing; null when the script leaves none, or leaves a doubt.</param>
internal sealed record Change<T>(Redefinition Why, T? Altered)
    where T : Module;
