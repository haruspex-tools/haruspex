namespace Haruspex.DifferentialTests.Validation;

/// <summary>A claim in the IR that the real output broke.</summary>
/// <param name="Procedure">The procedure's id, such as <c>sales.get_order</c>.</param>
/// <param name="Path">Where in its results, in the IR's path syntax with array indexes: <c>results[0][3].lines[0].note</c>.</param>
/// <param name="Claim">What the IR claimed there.</param>
/// <param name="Value">The value that broke it, as JSON, or <c>(absent)</c>.</param>
public sealed record Violation(string Procedure, string Path, string Claim, string Value)
{
    public override string ToString() => $"{Procedure} {Path}: the IR claims {Claim}, but the value was {Value}";
}
