using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The parameters declared to hold JSON of a schema, as shapes, each body that takes one recorded as reading it
/// (<see cref="DeclarationReads"/>), so that an object's claims list every declaration its results may rest on
/// (docs/decisions/0143-a-parameter-s-json-may-be-declared.md).
/// </summary>
/// <remarks>
/// <para>
/// A body that takes a declared parameter is recorded as reading it whether or not it reads it: which value a result holds is not
/// followed back to the parameter, so listing the claim for every body that takes one says no less than the results rest on.
/// </para>
/// <para>
/// What a caller sends and what the body reads differ for a <c>varchar</c> or <c>char</c> parameter: its text is in the database's
/// code page, so a character the code page lacks, sent as itself, arrives as another (observed:
/// <c>ParameterJsonObservationTests.A_varchar_parameter_s_json_loses_a_character_its_code_page_lacks_unless_it_is_escaped</c>, <c>Ω</c>
/// as <c>O</c>; sent through the driver, as <c>?</c>), though sent as a JSON escape it is kept. A caller may send either, so the body's
/// reads keep a string's value set only when every value is ASCII, and its lengths only then, as <c>OPENJSON … WITH</c> keeps a
/// <c>varchar</c> value set (<c>Typer.Converted</c>); what a caller sends keeps all the schema says.
/// </para>
/// </remarks>
internal sealed class DeclaredParameters
{
    private readonly Dictionary<Parameter, Entry> byParameter = new(ReferenceEqualityComparer.Instance);
    private readonly DeclarationReads reads;

    /// <summary>A declaration, whether its parameter's text passes through a code page, and its shapes once worked out.</summary>
    private sealed record Entry(DeclaredParameter Declared, bool CodePage)
    {
        public Shape? Sent { get; set; }

        public Shape? Read { get; set; }
    }

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
                byParameter.TryAdd(parameter, new Entry(declared, SchemaRules.ResolveType(model, parameter.Type)?.Type is { } type && type.IsAnyOf(BuiltInType.VarChar, BuiltInType.Char)));
        }
    }

    /// <summary>
    /// What the body <paramref name="reader"/> reads a non-null value of <paramref name="parameter"/> as, when it is declared; null when
    /// it isn't. The body, which takes it, is recorded as reading the declaration.
    /// </summary>
    public Shape? Content(Parameter parameter, SourceSpan reader)
    {
        if (!byParameter.TryGetValue(parameter, out var entry))
            return null;
        reads.Read(reader, IrOverrideSetting.ParameterJson, entry.Declared.Record.Parameter);
        if (!entry.CodePage)
            return Content(parameter);
        return entry.Read ??= Shape(entry.Declared, parameter, CodePageSafe);
    }

    /// <summary>What a non-null value of <paramref name="parameter"/> parses to, when it is declared, without recording a read: what a caller sends.</summary>
    public Shape? Content(Parameter parameter) =>
        byParameter.TryGetValue(parameter, out var entry) ? entry.Sent ??= Shape(entry.Declared, parameter, s => s) : null;

    private static Shape Shape(DeclaredParameter declared, Parameter parameter, Func<IrScalar, IrScalar> scalars) =>
        IrShapes.ToShape(
            IrTypeTree.MapScalars(declared.Content, scalars),
            declared.Types.ToEquatableMap(t => t.Key, t => t.Value with { Fields = [.. t.Value.Fields.Select(f => f with { Type = IrTypeTree.MapScalars(f.Type, scalars) })] }),
            $"The JSON {declared.Record.Parameter} is declared to hold",
            parameter.At);

    /// <summary>A string as a code page may give it back: its value set and lengths only when every value is ASCII.</summary>
    private static IrScalar CodePageSafe(IrScalar scalar) =>
        scalar is { Kind: IrScalarKind.String, Facets: { } facets } && !(facets.Values is { } values && values.Items.All(v => v.Text.All(c => c <= '\u007f')))
            ? scalar with { Facets = facets with { Values = null, Length = null, MinLength = null } }
            : scalar;
}
