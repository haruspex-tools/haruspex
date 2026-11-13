using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Haruspex.Analysers;

/// <summary>
/// Lets a switch expression over a closed hierarchy (<see cref="ClosedHierarchies"/>) leave out the discard arm, when its arms
/// cover every case. The compiler can't know that a hierarchy is closed, so it warns that such a switch is not exhaustive
/// (CS8509), which the build treats as an error; this suppresses that warning where every case is matched by an arm without a
/// condition, and only there. So a case added to a hierarchy fails the build at every switch over it that doesn't handle it,
/// as an enum's or a Swift enum's case would, rather than at run time.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExhaustiveSwitchSuppressor : DiagnosticSuppressor
{
    /// <summary>The suppression this gives: CS8509 on a switch over a closed hierarchy whose arms cover every case.</summary>
    public static readonly SuppressionDescriptor CoversEveryCase = new(
        "HXS0001",
        "CS8509",
        "The switch covers every case of a closed hierarchy (docs/decisions/0032-a-switch-over-a-closed-hierarchy-is-exhaustive.md).");

    /// <summary>The same for CS8846, which the compiler gives instead when an arm has a <c>when</c> clause.</summary>
    public static readonly SuppressionDescriptor CoversEveryCaseBesideGuards = new(
        "HXS0002",
        "CS8846",
        "The switch covers every case of a closed hierarchy with arms that have no when clause (docs/decisions/0032-a-switch-over-a-closed-hierarchy-is-exhaustive.md).");

    /// <inheritdoc/>
    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions { get; } = [CoversEveryCase, CoversEveryCaseBesideGuards];

    /// <inheritdoc/>
    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            if (Covers(diagnostic, context.GetSemanticModel, context.CancellationToken))
                context.ReportSuppression(Suppression.Create(diagnostic.Id == "CS8846" ? CoversEveryCaseBesideGuards : CoversEveryCase, diagnostic));
        }
    }

    private static bool Covers(Diagnostic diagnostic, Func<SyntaxTree, SemanticModel> model, CancellationToken cancel)
    {
        if (diagnostic.Location.SourceTree is not { } tree)
            return false;
        var node = tree.GetRoot(cancel).FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        var syntax = node.FirstAncestorOrSelf<SwitchExpressionSyntax>();
        if (syntax is null || model(tree).GetOperation(syntax, cancel) is not ISwitchExpressionOperation switchExpression)
            return false;

        var cases = ClosedHierarchies.Cases(switchExpression.Value.Type);
        if (cases.IsEmpty)
            return false;
        var covered = switchExpression.Arms.Where(a => a.Guard is null).SelectMany(a => Matched(a.Pattern)).ToList();
        return cases.All(c => covered.Any(m => SymbolEqualityComparer.Default.Equals(m, c) || ClosedHierarchies.Derives(c, m)));
    }

    /// <summary>
    /// The types a pattern matches whatever their values: a type, a declaration, or a property pattern whose every property
    /// pattern matches anything (<c>var x</c>, <c>_</c>); and either side of <c>or</c>.
    /// </summary>
    internal static IEnumerable<ITypeSymbol> Matched(IPatternOperation pattern) => pattern switch
    {
        ITypePatternOperation type => [type.MatchedType],
        IDeclarationPatternOperation { MatchedType: { } type } => [type],
        IRecursivePatternOperation { MatchedType: { } type } recursive
            when recursive.PropertySubpatterns.All(p => Anything(p.Pattern)) && recursive.DeconstructionSubpatterns.All(Anything) => [type],
        IBinaryPatternOperation { OperatorKind: BinaryOperatorKind.Or } or => Matched(or.LeftPattern).Concat(Matched(or.RightPattern)),
        _ => [],
    };

    /// <summary>Whether a pattern matches any value, null included: <c>_</c> or <c>var x</c>.</summary>
    private static bool Anything(IPatternOperation pattern) =>
        pattern is IDiscardPatternOperation or IDeclarationPatternOperation { MatchesNull: true };
}
