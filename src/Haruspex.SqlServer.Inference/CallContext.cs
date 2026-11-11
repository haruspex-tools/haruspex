using System.Collections.Immutable;

namespace Haruspex.SqlServer.Inference;

/// <summary>What a procedure's walk starts from, besides its own parameters and variables.</summary>
/// <param name="Temps">The caller's temporary tables, which the callee reads and writes as its own until it creates one of a name.</param>
/// <param name="Escapes">Whether to keep each state an error may leave the procedure from: the caller is inside a TRY.</param>
/// <param name="Inserting">
/// Whether the procedure runs under <c>INSERT … EXEC</c>, its result sets going into a table: FOR JSON fails there (13602)
/// and so does another <c>INSERT … EXEC</c> (8164), both observed.
/// </param>
internal sealed record CallContext(ImmutableDictionary<string, TempTable> Temps, bool Escapes, bool Inserting, bool ReadUncommitted = false)
{
    public static CallContext Top { get; } = new(ImmutableDictionary.Create<string, TempTable>(StringComparer.OrdinalIgnoreCase), false, false);
}
