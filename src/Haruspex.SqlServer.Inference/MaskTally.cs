namespace Haruspex.SqlServer.Inference;

/// <summary>
/// How many masked values a typer's expressions have given, and the column the last came from: how an expression tells that one
/// of its operands was masked, whatever rule typed it (<see cref="Typer.Expression"/>). It only counts. An expression reads the
/// count before its operands are typed and after, and the column only when the count moved, which an operand of its own moved; so
/// what any read gives depends only on that read, never on what was read before.
/// </summary>
internal sealed class MaskTally
{
    /// <summary>How many masked values have been given.</summary>
    public int Count { get; private set; }

    /// <summary>The masked column the last one came from; null before any.</summary>
    public string? Last { get; private set; }

    /// <summary>Counts a masked value from <paramref name="column"/>.</summary>
    public void Add(string column)
    {
        Count++;
        Last = column;
    }
}
