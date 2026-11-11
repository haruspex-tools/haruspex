namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The integer digits SQL Server may count for an int constant: exactly <paramref name="Least"/> when it equals
/// <paramref name="Most"/>, otherwise any number between, depending on whether it folds the expression to a literal.
/// </summary>
internal sealed record IntDigits(int Least, int Most)
{
    /// <summary>A CASE-like expression the server may fold to one of these values, or leave as an int of 10 digits.</summary>
    public static IntDigits? Foldable(IEnumerable<IntDigits?> results)
    {
        var known = results.Where(d => d is not null).Select(d => d!).ToList();
        return known.Count == 0 ? null : new IntDigits(known.Min(d => d.Least), 10);
    }
}
