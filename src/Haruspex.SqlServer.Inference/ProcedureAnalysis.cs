namespace Haruspex.SqlServer.Inference;

/// <summary>What inference found in one procedure.</summary>
/// <param name="Parameters">Each declared parameter and its type.</param>
/// <param name="Results">The result sets in order, or null when a statement makes them impossible to list.</param>
/// <param name="ResultsUnknown">Why the results cannot be listed, when <paramref name="Results"/> is null.</param>
/// <param name="Declared">The procedures whose declared results its results include, through its calls.</param>
internal sealed record ProcedureAnalysis(
    IReadOnlyList<ParameterShape> Parameters,
    IReadOnlyList<ResultShape>? Results,
    UnknownShape? ResultsUnknown,
    IReadOnlyCollection<string> Declared);
