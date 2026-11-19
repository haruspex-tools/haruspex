namespace Haruspex.Ir;

/// <summary>How the server fills a column of a table type that a row sent can't (0.26).</summary>
public enum IrColumnGeneration
{
    /// <summary>An identity column, which the server numbers.</summary>
    Identity,

    /// <summary>A computed column, which the server computes from the row's other columns.</summary>
    Computed,

    /// <summary>A <c>rowversion</c> column, which the server stamps.</summary>
    Rowversion,
}
