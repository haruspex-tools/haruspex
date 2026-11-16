using Haruspex.Collections;
using Haruspex.Ir;

namespace Haruspex.Emit.JsonSchema.Lowering;

/// <summary>
/// Where a schema is, in the IR's terms: the object and the path the document's unknowns name places by (<see cref="IrPath"/>), so
/// that an unknown's schema can say why, in the IR's own sentence.
/// </summary>
/// <param name="Object">The object the path is in: a procedure's id, or the function a recursive type is shared under.</param>
/// <param name="Segments">The path within it.</param>
internal sealed record Place(string Object, EquatableArray<IrPathSegment> Segments)
{
    /// <summary>The place of what <paramref name="segment"/> leads to; the same place when there is no segment (a union's variants share it).</summary>
    public Place Then(IrPathSegment? segment) => segment is null ? this : this with { Segments = [.. Segments, segment] };

    /// <summary>The path as the IR writes it.</summary>
    public string Path => IrPath.Render(Segments);

    /// <summary>
    /// Whether the path is inside a type several objects share (<c>&lt;bom.part_tree&gt;.part</c>), whose unknowns are listed under
    /// every object whose results reach it.
    /// </summary>
    public bool IsShared => Segments.Count > 0 && Segments[0] is IrPathShared;

    /// <summary>The place of a named type, from its identity; null for a type built by hand, which has none.</summary>
    public static Place? Of(IrObjectType type) =>
        type.Identity is { } identity && IrPath.TryParse(identity.Path, out var segments, out _) ? new Place(identity.Object, [.. segments]) : null;
}
