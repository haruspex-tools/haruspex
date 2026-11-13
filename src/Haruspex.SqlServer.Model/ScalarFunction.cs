using Haruspex.Collections;
using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>A function that returns one value (<c>RETURNS int</c>).</summary>
/// <param name="Name">Its name.</param>
/// <param name="Span">Where its definition is.</param>
/// <param name="Parameters">Its parameters, in declaration order.</param>
/// <param name="Returns">Its declared return type; null when the declaration names a type ScriptDom gives no name.</param>
/// <param name="Body">What it runs.</param>
public sealed record ScalarFunction(ObjectName Name, SourceSpan Span, EquatableArray<Parameter> Parameters, SqlType? Returns, ModuleBody Body)
    : Function(Name, Span, Parameters)
{
    /// <summary>
    /// Whether it is declared <c>RETURNS NULL ON NULL INPUT</c>, so that SQL Server returns NULL without running the body when
    /// any argument is NULL.
    /// </summary>
    public bool ReturnsNullOnNullInput { get; init; }
}
