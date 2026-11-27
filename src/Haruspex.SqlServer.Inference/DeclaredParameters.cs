using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The parameters declared to hold JSON of a schema, as shapes, each body that takes one recorded as reading it
/// (<see cref="DeclarationReads"/>), so that an object's claims list every declaration its results may rest on
/// (docs/decisions/0143-a-parameter-s-json-may-be-declared.md).
/// </summary>
/// <remarks>
/// A body that takes a declared parameter is recorded as reading it whether or not it reads it: which value a result holds is not
/// followed back to the parameter, so listing the claim for every body that takes one says no less than the results rest on.
/// </remarks>
internal sealed class DeclaredParameters
{
    private readonly Dictionary<Parameter, (DeclaredParameter Declared, Shape? Content)> byParameter = new(ReferenceEqualityComparer.Instance);
    private readonly DeclarationReads reads;

    /// <param name="model">The model the parameters are looked up in: a declaration of a parameter it doesn't have is never read.</param>
    /// <param name="parameters">The declarations.</param>
    /// <param name="reads">Where each body that takes one is recorded.</param>
    public DeclaredParameters(DatabaseModel model, IReadOnlyList<DeclaredParameter> parameters, DeclarationReads reads)
    {
        this.reads = reads;
        foreach (var declared in parameters)
        {
            var taken = model.FindProcedure(declared.Owner)?.Parameters ?? model.FindFunction(declared.Owner)?.Parameters ?? [];
            if (taken.FirstOrDefault(p => p.Name.Equals(declared.Parameter, StringComparison.OrdinalIgnoreCase)) is { } parameter)
                byParameter.TryAdd(parameter, (declared, null));
        }
    }

    /// <summary>
    /// What a non-null value of <paramref name="parameter"/> parses to, when it is declared; null when it isn't. The body
    /// <paramref name="reader"/>, which takes it, is recorded as reading the declaration.
    /// </summary>
    public Shape? Content(Parameter parameter, SourceSpan reader)
    {
        if (Content(parameter) is not { } content)
            return null;
        reads.Read(reader, IrOverrideSetting.ParameterJson, byParameter[parameter].Declared.Record.Parameter);
        return content;
    }

    /// <summary>What a non-null value of <paramref name="parameter"/> parses to, when it is declared, without recording a read: what a caller sends.</summary>
    public Shape? Content(Parameter parameter)
    {
        if (!byParameter.TryGetValue(parameter, out var found))
            return null;
        if (found.Content is null)
        {
            found = found with { Content = IrShapes.ToShape(found.Declared.Content, found.Declared.Types, $"The JSON {found.Declared.Record.Parameter} is declared to hold", parameter.At) };
            byParameter[parameter] = found;
        }

        return found.Content;
    }
}
