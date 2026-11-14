using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>Where the shapes were inferred from: the dialect, and what the callers' sessions are declared to set.</summary>
/// <param name="Dialect">The SQL dialect of the source, such as <c>tsql</c>.</param>
public sealed record IrSource(string Dialect)
{
    /// <summary>
    /// The options the callers' sessions are declared to set, sorted by name (0.17). Empty when none is declared: then every
    /// claim holds whatever the callers set, as it did in every version before 0.17, and the file leaves <c>session</c> out.
    /// </summary>
    public EquatableArray<IrSessionOption> Session { get; init; } = [];
}
