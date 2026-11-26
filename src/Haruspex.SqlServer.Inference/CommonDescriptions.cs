namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Two shapes that claim the same may still differ in their fields' descriptions, which equality leaves out
/// (<see cref="FieldShape"/>): where they are merged, the field may hold either one's value, so a description is kept only where
/// both give the same (docs/decisions/0128-a-column-s-description-reaches-the-fields-that-are-that-column.md).
/// </summary>
internal static class CommonDescriptions
{
    /// <summary>
    /// <paramref name="a"/>, with each field's description kept only where <paramref name="b"/>'s field in the same place has the
    /// same; <paramref name="a"/> as it is when the two aren't alike in shape, which a caller merging equal shapes never meets.
    /// </summary>
    public static Shape Of(Shape a, Shape b) => (a, b) switch
    {
        _ when ReferenceEquals(a, b) => a,
        (ObjectShape x, ObjectShape y) when x.Fields.Count == y.Fields.Count => x with
        {
            Fields = [.. x.Fields.Zip(y.Fields, (f, g) => f with { Type = Of(f.Type, g.Type), Description = Same(f.Description, g.Description) })],
        },
        (ArrayShape x, ArrayShape y) => x with { Items = Of(x.Items, y.Items) },
        (TupleShape x, TupleShape y) when x.Items.Count == y.Items.Count => x with
        {
            Items = [.. x.Items.Zip(y.Items, (p, q) => p with { Type = Of(p.Type, q.Type) })],
        },
        (RecordShape x, RecordShape y) => x with { Values = Of(x.Values, y.Values) },
        (UnionShape x, UnionShape y) when x.Variants.Count == y.Variants.Count => x with
        {
            Variants = [.. x.Variants.Zip(y.Variants, Of)],
        },
        (JsonTextShape { Content: { } p } x, JsonTextShape { Content: { } q }) => x with { Content = Of(p, q) },
        (TableRowShape x, TableRowShape y) => x with { Row = (ObjectShape)Of(x.Row, y.Row) },
        _ => a,
    };

    /// <summary>
    /// <paramref name="shape"/> with no description anywhere in it: a variant of a union that joining made, which a value of
    /// another variant may also match (an owner's name short enough to pass as a dog's), so no variant's field says for certain
    /// which column it holds.
    /// </summary>
    public static Shape None(Shape shape) => shape switch
    {
        ObjectShape x => x with { Fields = [.. x.Fields.Select(f => f with { Type = None(f.Type), Description = null })] },
        ArrayShape x => x with { Items = None(x.Items) },
        TupleShape x => x with { Items = [.. x.Items.Select(p => p with { Type = None(p.Type) })] },
        RecordShape x => x with { Values = None(x.Values) },
        UnionShape x => x with { Variants = [.. x.Variants.Select(None)] },
        JsonTextShape { Content: { } content } x => x with { Content = None(content) },
        TableRowShape x => x with { Row = (ObjectShape)None(x.Row) },
        _ => shape,
    };

    /// <summary>A description both give alike; null where they differ or either has none.</summary>
    public static string? Same(string? a, string? b) => string.Equals(a, b, StringComparison.Ordinal) ? a : null;
}
