using Haruspex.Collections;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// A variable whose name another the body declares, or a parameter's, differs from only in case (<c>@Total</c> and
/// <c>@total</c>). Names are matched ignoring case (<see cref="LocalName"/>), but two such names can be declared only where the
/// server compares names by case (a case-insensitive server refuses the second, Msg 134), which haruspex doesn't model, so which
/// one a reference means is not known (decision 0107).
/// </summary>
/// <param name="Name">The name as written.</param>
/// <param name="Variants">Every spelling the body declares: a parameter's, then each variable's, then each table variable's.</param>
public sealed record CaseVariantVariable(LocalName Name, EquatableArray<string> Variants) : VariableResolution
{
    /// <inheritdoc/>
    public override LocalName Name { get; } = Name;

    /// <summary>Why a reference to it is unknown.</summary>
    public string Why => $"The body declares {string.Join(" and ", Variants)}, whose names differ only in case: names are compared by case there, which haruspex doesn't model, so which one {Name} means is not known.";
}
