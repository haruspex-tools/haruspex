using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Config;

/// <summary>
/// A procedure's results declared in the config file (<c>procedures["s.p"].results</c>), translated into the IR: the
/// result sets, in order, and the object types they refer to.
/// </summary>
/// <param name="Results">The result sets; empty for <c>"none"</c>.</param>
/// <param name="Types">
/// The object types <paramref name="Results"/> refer to, keyed provisionally as inference keys its own (by the procedure and
/// path that own each, <see cref="IrObjectType.Identity"/>); names are settled with everything else's.
/// </param>
/// <param name="Why">Why the declaration holds.</param>
/// <param name="At">Where <c>results</c> is written.</param>
public sealed record ResultsDeclaration(EquatableArray<IrResult> Results, EquatableMap<string, IrObjectType> Types, string Why, JsonLocation At);
