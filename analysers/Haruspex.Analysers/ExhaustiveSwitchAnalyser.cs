using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Haruspex.Analysers;

/// <summary>
/// Switches over a closed hierarchy (<see cref="ClosedHierarchies"/>). A switch statement without a <c>default</c> must handle
/// every case (HX0001): the compiler checks no switch statement for completeness, so this does, as
/// <see cref="ExhaustiveSwitchSuppressor"/> lets the compiler's own check stand for a switch expression. And a switch that
/// handles every case may not also have a discard arm, or a <c>default</c> that only throws (HX0002): that arm stands for
/// "can't happen", or quietly takes a case added later, and either way keeps it from failing the build at the switch that
/// forgot it. And a case of a closed hierarchy that other assemblies can name must be one they can name (HX0003).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExhaustiveSwitchAnalyser : DiagnosticAnalyzer
{
    /// <summary>A switch statement over a closed hierarchy that leaves cases out.</summary>
    public static readonly DiagnosticDescriptor MissesCases = new(
        "HX0001",
        "A switch over a closed hierarchy leaves cases out",
        "The switch over {0} handles neither {1} nor every case with default",
        "Correctness",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A switch statement over a closed hierarchy handles every case, or says what happens to the rest with default.");

    /// <summary>A switch over a closed hierarchy that handles every case and also throws for the rest.</summary>
    public static readonly DiagnosticDescriptor ThrowsForNoCase = new(
        "HX0002",
        "A switch over a closed hierarchy that handles every case has an arm for none",
        "The switch over {0} handles every case, so its arm for the rest can never run: remove it, so a case added later fails the build here",
        "Correctness",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A switch over a closed hierarchy that handles every case has no discard arm, and no default that only throws.");

    /// <summary>A case of a closed hierarchy that code in another assembly can't name, though it can name the hierarchy.</summary>
    public static readonly DiagnosticDescriptor HiddenCase = new(
        "HX0003",
        "A case of a closed hierarchy is less visible than the hierarchy",
        "{0} is a case of {1}, which other assemblies can name, but they can't name {0}: their switches over {1} would take themselves to be complete without it",
        "Correctness",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Every case of a closed hierarchy that other assemblies can name can be named by them too.");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [MissesCases, ThrowsForNoCase, HiddenCase];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(Check, OperationKind.Switch);
        context.RegisterOperationAction(CheckExpression, OperationKind.SwitchExpression);
        context.RegisterSymbolAction(CheckCase, SymbolKind.NamedType);
    }

    /// <summary>
    /// In the assembly that declares a closed hierarchy, a case of it less visible than it (HX0003): another assembly compiles
    /// against what it can see, so a switch there over the hierarchy would be taken to cover every case without that one.
    /// </summary>
    private static void CheckCase(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (ClosedHierarchies.IsVisibleOutside(type))
            return;
        for (var root = type.BaseType; root is not null; root = root.BaseType)
        {
            if (ClosedHierarchies.IsRoot(root) && ClosedHierarchies.IsVisibleOutside(root))
            {
                context.ReportDiagnostic(Diagnostic.Create(HiddenCase, type.Locations[0], type.Name, root.Name));
                return;
            }
        }
    }

    private static void CheckExpression(OperationAnalysisContext context)
    {
        var expression = (ISwitchExpressionOperation)context.Operation;
        var cases = ClosedHierarchies.Cases(expression.Value.Type);
        if (cases.IsEmpty)
            return;
        // A discard over a value that may be null is its null arm, not one for no case.
        if (MayBeNull(expression.Value))
            return;
        var unguarded = expression.Arms.Where(a => a.Guard is null).ToList();
        var covered = unguarded.Where(a => a.Pattern is not IDiscardPatternOperation).SelectMany(a => Matched(a.Pattern)).ToList();
        if (unguarded.FirstOrDefault(a => a.Pattern is IDiscardPatternOperation) is { } discard
            && Missing(cases, covered).Count == 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(ThrowsForNoCase, discard.Syntax.GetLocation(), expression.Value.Type!.Name));
        }
    }

    private static void Check(OperationAnalysisContext context)
    {
        var statement = (ISwitchOperation)context.Operation;
        var cases = ClosedHierarchies.Cases(statement.Value.Type);
        if (cases.IsEmpty)
            return;

        var clauses = statement.Cases.SelectMany(c => c.Clauses).ToList();
        var covered = clauses.OfType<IPatternCaseClauseOperation>().Where(c => c.Guard is null).SelectMany(c => Matched(c.Pattern)).ToList();
        var missing = Missing(cases, covered);
        if (statement.Cases.FirstOrDefault(c => c.Clauses.Any(k => k is IDefaultCaseClauseOperation)) is { } fallback)
        {
            if (missing.Count == 0 && !MayBeNull(statement.Value) && fallback.Body.Length == 1 && fallback.Body[0] is IThrowOperation or IExpressionStatementOperation { Operation: IThrowOperation })
                context.ReportDiagnostic(Diagnostic.Create(ThrowsForNoCase, fallback.Syntax.GetLocation(), statement.Value.Type!.Name));
            return;
        }

        if (missing.Count > 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                MissesCases,
                statement.Syntax.GetLocation(),
                statement.Value.Type!.Name,
                string.Join(", ", missing.Select(m => m.Name))));
        }
    }

    /// <summary>Whether an arm's value only throws: a throw expression, which the compiler wraps in a conversion to the switch's type.</summary>
    private static bool Throws(IOperation value) => value switch
    {
        IThrowOperation => true,
        IConversionOperation { Operand: var operand } => Throws(operand),
        _ => false,
    };

    /// <summary>Whether the compiler's nullable analysis says a value may be null where it is switched on.</summary>
    private static bool MayBeNull(IOperation value) =>
        value.SemanticModel?.GetTypeInfo(value.Syntax).Nullability.FlowState != NullableFlowState.NotNull;

    /// <summary>The cases none of <paramref name="covered"/> matches whatever its value.</summary>
    private static List<INamedTypeSymbol> Missing(ImmutableArray<INamedTypeSymbol> cases, List<ITypeSymbol> covered) =>
        [.. cases.Where(c => !covered.Any(m => SymbolEqualityComparer.Default.Equals(m, c) || ClosedHierarchies.Derives(c, m)))];

    /// <summary>The types a pattern matches whatever their values (<see cref="ExhaustiveSwitchSuppressor"/> reads them the same way).</summary>
    private static IEnumerable<ITypeSymbol> Matched(IPatternOperation pattern) => ExhaustiveSwitchSuppressor.Matched(pattern);
}
