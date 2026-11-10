using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Inference;

/// <summary>Infers the IR for procedures of a model.</summary>
public static class IrInference
{
    /// <summary>Infers the IR for the given procedures of <paramref name="model"/>.</summary>
    /// <param name="model">The model.</param>
    /// <param name="procedures">The procedures to infer.</param>
    /// <param name="declarations">
    /// The results declared for procedures whose results cannot be inferred: a call of one contributes these instead of
    /// making its caller's results unknown. A procedure's own declaration is not applied to it here; that is the config
    /// layer's, which also checks it against what is inferred.
    /// </param>
    /// <exception cref="ArgumentException">A name is not a procedure of the model.</exception>
    public static IrDocument Infer(DatabaseModel model, IReadOnlyList<ObjectName> procedures, IReadOnlyDictionary<ObjectName, DeclaredResults>? declarations = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(procedures);
        declarations ??= new Dictionary<ObjectName, DeclaredResults>();
        var functions = new FunctionSummaries(model);
        var calls = new ProcedureCalls(model, functions, declarations);
        var analysed = procedures
            .Distinct()
            .Select(name => model.FindProcedure(name)
                ?? throw new ArgumentException($"No procedure named {name} in the project.", nameof(procedures)))
            .Select(procedure => (procedure, calls.Analyse(procedure)))
            .ToList();
        var document = new IrBuilder(functions).Build(analysed);

        // A caller's results that include a callee's declared results rest on that declaration, so the claim is listed.
        var claims = analysed
            .Where(a => a.Item2.Results is not null)
            .SelectMany(a => a.Item2.Declared.Select(callee => (Object: a.procedure.Name.ToString(), Callee: callee)))
            .Select(c => new IrOverride(c.Object, "results", IrOverrideSettings.Results, declarations.First(d => d.Key.ToString() == c.Callee).Value.Why) { Via = c.Callee })
            .ToList();
        return document with { Overrides = claims };
    }
}
