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
}
