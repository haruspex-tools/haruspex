using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>A typed object: a procedure of the database, or a query file of the application.</summary>
/// <param name="Id">
/// The object's canonical identity: a procedure's name in the database, such as <c>sales.get_order</c>; a query file's path,
/// relative to the folder of the config file it belongs to, with <c>/</c> between folders, such as <c>queries/getOrderCard.sql</c>.
/// </param>
/// <param name="Name">
/// The name settled for generated code, such as <c>GetOrder</c>, from the config file's naming rules and overrides.
/// Emitters use it as is. A front end leaves the id here until the names are settled.
/// </param>
/// <param name="Kind">What kind of object it is.</param>
/// <param name="At">Where it is defined, as <c>path:line:column</c> relative to the source root.</param>
/// <param name="Parameters">
/// The parameters, in order: a procedure's as declared; a query file's as its text first uses them, each typed as the file
/// declares it or else from its uses.
/// </param>
/// <param name="Results">The result sets in the order the object returns them, or null when they cannot be listed.</param>
public sealed record IrObject(
    string Id,
    string Name,
    IrObjectKind Kind,
    string At,
    EquatableArray<IrParameter> Parameters,
    EquatableArray<IrResult>? Results)
{
    /// <summary>
    /// What a query file runs, as one batch (0.22): its text as read, with LF line endings. Null for a procedure, which is run by
    /// its name. A query's parameters are declared to the call by their names and their types' <c>sql</c>, as its function sends them.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Whether a call may end with a return code other than 0 (0.22): its body has a <c>RETURN</c> whose value isn't the constant
    /// 0 or NULL, or can't be read. False when every call ends with 0, as one without such a <c>RETURN</c> does (observed: no
    /// <c>RETURN</c>, a bare one, <c>RETURN 0</c> and <c>RETURN NULL</c> all give 0), so the code tells a caller nothing.
    /// </summary>
    public bool ReturnsCode { get; init; }

    /// <summary>
    /// The names of its result sets, in order, when the config file named them (<see cref="IrResult.Name"/>); null when they are
    /// unnamed or can't be listed. An object's sets are named all or none, so one name means every set has one.
    /// </summary>
    public EquatableArray<string>? SetNames =>
        Results is { Count: > 0 } results && results.All(r => r.Name is not null) ? [.. results.Select(r => r.Name).OfType<string>()] : null;
}
