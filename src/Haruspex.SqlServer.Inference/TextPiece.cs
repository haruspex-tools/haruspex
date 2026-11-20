namespace Haruspex.SqlServer.Inference;

/// <summary>
/// A piece of a string's text, as far as the string may be JSON assembled as text: what <see cref="JsonAssembly"/> reads to
/// say which JSON, if any, the whole makes. A string's pieces (<see cref="Value.Pieces"/>) are its text when it is not null, in
/// order; the README in this folder, "JSON assembled as text", says what each comes from.
/// </summary>
/// <remarks>A closed set: the kinds in this assembly are all there are (decision 0031).</remarks>
internal abstract record TextPiece
{
    /// <summary>Only this assembly derives a piece, so the set is closed.</summary>
    private protected TextPiece()
    {
    }
}
