using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>A key of a table type (0.25): columns whose values no two rows may share.</summary>
/// <param name="Columns">The key's columns, by name, in key order.</param>
public sealed record IrTableKey(EquatableArray<string> Columns)
{
    /// <summary>
    /// Whether a row whose key another row already has is dropped without an error (<c>IGNORE_DUP_KEY = ON</c>) rather than refusing
    /// the call. The key holds of the rows kept either way.
    /// </summary>
    public bool IgnoresDuplicates { get; init; }
}
