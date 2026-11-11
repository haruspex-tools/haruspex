namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The JSON node of a function that calls itself, directly or through others: a reference to a named type that the
/// IR's type table defines once (<see cref="FunctionSummaries.Nodes"/>).
/// </summary>
/// <param name="Function">The function whose node it is.</param>
internal sealed record RecursiveShape(Model.ObjectName Function) : Shape;
