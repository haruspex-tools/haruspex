using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics.Collations;

namespace Haruspex.SqlServer.Inference;

/// <summary>Infers the IR for procedures of a model, and for query files read against it.</summary>
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
    /// <param name="session">
    /// The options the callers' connections are declared to set; not known when null, so every rule that depends on one claims
    /// what holds under any value. The document records it.
    /// </param>
    /// <returns>
    /// The document, with what could not be checked, once each: a write to a table the project does not define, whose
    /// triggers (which could return result sets) are not in the project. Inference always gives a document, since what it
    /// can't type is an unknown in it, so it returns a success, and says so in its type.
    /// </returns>
    /// <param name="queries">
    /// The application's query files to infer beside the procedures, each read against <paramref name="model"/>; none when null.
    /// </param>
    /// <exception cref="ArgumentException">A name is not a procedure of the model: a fault of the caller, which selected it.</exception>
    public static Succeeded<IrDocument> Infer(
        DatabaseModel model,
        IReadOnlyList<ObjectName> procedures,
        IReadOnlyDictionary<ObjectName, DeclaredResults>? declarations = null,
        SessionOptions? session = null,
        IReadOnlyList<QueryFile>? queries = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(procedures);
        queries ??= [];
        declarations ??= new Dictionary<ObjectName, DeclaredResults>();
        session ??= SessionOptions.NotKnown;
        var warnings = new List<Diagnostic>();
        var summaries = new Summaries(model, declarations, warnings, session);
        var analysed = procedures
            .Distinct()
            .Select(name => model.FindProcedure(name)
                ?? throw new ArgumentException($"No procedure named {name} in the project.", nameof(procedures)))
            .Select(procedure => (procedure, summaries.Analyse(procedure)))
            .ToList();
        var files = new QueryFiles(model, summaries);
        var queried = queries.Select(query => (query, files.Analyse(query))).ToList();
        var document = new IrBuilder(summaries.Nodes).Build(analysed, queried, session);

        // A caller's results that include a callee's declared results rest on that declaration, so the claim is listed.
        var claims = analysed.Select(a => (Object: a.procedure.Name.ToString(), Analysis: a.Item2))
            .Concat(queried.Select(q => (Object: q.query.Path, Analysis: q.Item2)))
            .Where(a => a.Analysis.Results is not null)
            .SelectMany(a => a.Analysis.Declared.Select(callee => (a.Object, Callee: callee)))
            .Select(c => new IrOverride(c.Object, "results", IrOverrideSetting.Results, declarations[c.Callee].Why) { Via = c.Callee.ToString() })
            .ToList();
        var folds = IrDocumentFolds.Named(document).ToEquatableMap(name => name, name => SqlTypes.Fold(CollationFold.Named(name)));
        return new Succeeded<IrDocument>(document with { Overrides = [.. claims], Folds = folds }, [.. warnings]);
    }
}
