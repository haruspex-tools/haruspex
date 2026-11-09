using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Inference;

/// <summary>Infers the IR for procedures of a model.</summary>
public static class IrInference
{
    /// <summary>Infers the IR for the given procedures of <paramref name="model"/>.</summary>
    /// <exception cref="ArgumentException">A name is not a procedure of the model.</exception>
    public static IrDocument Infer(DatabaseModel model, IReadOnlyList<ObjectName> procedures)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(procedures);
        var functions = new FunctionSummaries(model);
        var analysed = procedures
            .Distinct()
            .Select(name => model.FindProcedure(name)
                ?? throw new ArgumentException($"No procedure named {name} in the project.", nameof(procedures)))
            .Select(procedure => (procedure, Analyse(model, functions, procedure)))
            .ToList();
        return new IrBuilder(functions).Build(analysed);
    }

    private static ProcedureAnalysis Analyse(DatabaseModel model, FunctionSummaries functions, Procedure procedure)
    {
        var analysis = new ProcedureAnalyzer(model, procedure.File, functions).Analyse(procedure.Body);
        if (procedure.Redefinition is not { } redefinition)
            return analysis;

        // The parameters are still read from the definition the model kept, but which body runs depends on the
        // order the scripts run, so the results are withdrawn.
        var why = new UnknownShape(IrUnknownReasons.Redefined, redefinition.Message, redefinition.At);
        return analysis with { Results = null, ResultsUnknown = why };
    }
}
