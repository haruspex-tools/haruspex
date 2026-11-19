namespace Haruspex.Emit.Mssql.Planning;

/// <summary>How a function sends one of its procedure's parameters.</summary>
/// <param name="Key">The parameter's key in the parameters object, and its name to the driver: its declared name without its <c>@</c>.</param>
/// <param name="Type">The driver's type it is sent as; null to let the driver choose, for a parameter whose type isn't known.</param>
/// <param name="IsOutput">Whether it is an <c>OUTPUT</c> parameter, bound so that its value comes back.</param>
/// <param name="OnlyWhenGiven">
/// Whether it is bound only when the caller gives it. A parameter with a default left out is not bound at all: bound as
/// <c>undefined</c>, the driver sends NULL, which overrides the default (observed).
/// </param>
internal sealed record Binding(string Key, DriverType? Type, bool IsOutput, bool OnlyWhenGiven)
{
    /// <summary>For a table-valued parameter, the table its rows are sent as; null for every other parameter.</summary>
    public TableValue? Table { get; init; }
}
