using Haruspex.Ir;

namespace Haruspex.TestSupport;

/// <summary>
/// Finding things in an inferred <see cref="IrDocument"/>: a procedure's results, a row's fields, a field's reasons to be
/// null, an unknown. Test classes import it with <c>using static</c>, so a test reads <c>Field(doc, "name")</c>.
/// </summary>
/// <remarks>
/// A test class that needs a stricter or different lookup (one that asserts there is a single result, say) keeps its own
/// method of the same name, which hides this one, and says how it differs.
/// </remarks>
internal static class IrNavigation
{
    /// <summary>The results of the document's first procedure, failing with the unknowns' details when they are unknown.</summary>
    public static IReadOnlyList<IrResult> Results(IrDocument doc) =>
        doc.Objects[0].Results ?? throw new InvalidOperationException("The results are unknown: " + string.Join("; ", doc.Unknowns.Select(u => u.Detail)));

    /// <summary>The procedure with this id.</summary>
    public static IrObject Object(IrDocument doc, string id) => Assert.Single(doc.Objects, o => o.Id == id);

    /// <summary>The fields of an object type, or of an array's items.</summary>
    public static IReadOnlyList<IrField> Fields(IrDocument doc, IrType type) => type switch
    {
        IrArray { Items: var items } => Fields(doc, items),
        IrRef r => doc.Types[r.Name].Fields,
        var other => throw new InvalidOperationException($"Unexpected type {other}."),
    };

    /// <summary>The fields of a result of the document's first procedure.</summary>
    public static IReadOnlyList<IrField> Fields(IrDocument doc, int result = 0) => Fields(doc, Results(doc)[result].Type);

    /// <summary>The field of this name in a result of the document's first procedure.</summary>
    public static IrField Field(IrDocument doc, string name, int result = 0) => Assert.Single(Fields(doc, result), f => f.Name == name);

    /// <summary>The names of a type's fields, in order.</summary>
    public static string[] Names(IrDocument doc, IrType type) => [.. Fields(doc, type).Select(f => f.Name)];

    /// <summary>The rules that make a field nullable, such as <c>nullable-column</c>.</summary>
    public static IrNullRule[] Rules(IrField field) => [.. field.NullableBecause.Select(r => r.Rule)];

    /// <summary>The rules that make the named field of the first result nullable.</summary>
    public static IrNullRule[] Rules(IrDocument doc, string name) => Rules(Field(doc, name));

    /// <summary>The unknown at this path.</summary>
    public static IrUnknown Unknown(IrDocument doc, string path) => Assert.Single(doc.Unknowns, u => u.Path == path);

    /// <summary>Asserts the unknown at this path has this reason.</summary>
    public static void AssertUnknown(IrDocument doc, string path, IrUnknownReason reason) => Assert.Equal(reason, Unknown(doc, path).Reason);

    /// <summary>Asserts nothing in the document is unknown, listing what is.</summary>
    public static void AllTyped(IrDocument doc) =>
        Assert.True(doc.Unknowns.Count == 0, "Unknowns: " + string.Join("; ", doc.Unknowns.Select(u => $"{u.Path}: {u.Detail}")));
}
