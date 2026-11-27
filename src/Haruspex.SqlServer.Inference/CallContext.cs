using Haruspex.Collections;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>What a procedure's walk starts from, besides its own parameters and variables.</summary>
/// <param name="Temps">The caller's temporary tables, which the callee reads and writes as its own until it creates one of a name.</param>
/// <param name="Escapes">Whether to keep each state an error may leave the procedure from: the caller is inside a TRY.</param>
/// <param name="Inserting">
/// Whether the procedure runs under <c>INSERT … EXEC</c>, its result sets going into a table: FOR JSON fails there (13602)
/// and so does another <c>INSERT … EXEC</c> (8164), both observed.
/// </param>
internal sealed record CallContext(EquatableMap<LocalName, TempTable> Temps, bool Escapes, bool Inserting, bool ReadUncommitted = false)
{
    /// <summary>
    /// The text of each argument the call passes a string parameter (or the default it takes), where every text it may be is known
    /// and fits the parameter. Passed only into a procedure that calls a REST endpoint (<see cref="ProcedureCalls.CallsRest"/>),
    /// whose URL may be built from it, so that a wrapper is typed at each call of it
    /// (docs/decisions/0140-a-rest-response-is-typed-from-a-schema-declared-for-its-endpoint.md); empty otherwise, so that no other
    /// walk is told apart by its arguments.
    /// </summary>
    public EquatableMap<LocalName, Passed> Arguments { get; init; } = [];

    /// <summary>The text a call passes a parameter.</summary>
    /// <param name="Pieces">What its text is made of when it is not NULL.</param>
    /// <param name="MayBeNull">Whether it may be NULL: a parameter's value is the argument's, so it is NULL only where the argument may be.</param>
    internal sealed record Passed(EquatableArray<TextPiece> Pieces, bool MayBeNull);

    public static CallContext Top { get; } = new([], false, false);
}
