using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>A typed database object.</summary>
/// <param name="Id">The object's name in the database, such as <c>sales.get_order</c>: its canonical identity.</param>
/// <param name="Name">
/// The name settled for generated code, such as <c>GetOrder</c>, from the config file's naming rules and overrides.
/// Emitters use it as is. A front end leaves the id here until the names are settled.
/// </param>
/// <param name="Kind">What kind of object it is.</param>
/// <param name="At">Where it is defined, as <c>path:line:column</c> relative to the source root.</param>
/// <param name="Parameters">The declared parameters, in order.</param>
/// <param name="Results">The result sets in the order the object returns them, or null when they cannot be listed.</param>
public sealed record IrObject(
    string Id,
    string Name,
    IrObjectKind Kind,
    string At,
    EquatableArray<IrParameter> Parameters,
    EquatableArray<IrResult>? Results);
