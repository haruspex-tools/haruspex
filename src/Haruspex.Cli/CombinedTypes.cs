using Haruspex.Collections;
using Haruspex.Config;
using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Cli;

/// <summary>
/// The module's document combined with its baseline's (<see cref="IrCombination"/>), so that every emitter writes types that work
/// against both databases: the one before the change is deployed and the one after. What can't be bridged is an error at its
/// procedure, which the module leaves out while it is still written for the rest, so one stuck procedure doesn't hold up the
/// others; and the run fails. Why: docs/decisions/0073-generate-with-a-baseline-writes-types-that-hold-against-both-databases.md.
/// </summary>
internal static class CombinedTypes
{
    /// <summary>
    /// The document combined with the baseline's, and what combining them found, each at its procedure; refused when the
    /// baseline is itself a combined document, which describes an application, not a database.
    /// </summary>
    public static Outcome<(IrDocument Document, EquatableArray<Diagnostic> Found)> Of(IrDocument own, Baseline baseline, HaruspexConfig config, SqlProject project, string shown)
    {
        if (baseline.Document.Source.Baseline is not null)
        {
            return ModuleRun.Refused<(IrDocument, EquatableArray<Diagnostic>)>(CliCodes.BaselineCombined,
                $"the baseline {shown} is a combined IR, written with a baseline of its own: it says what an application built against two databases accepts, not what one database is. Give the database the application meets before the change, as its source (a .dacpac, a .sqlproj or a folder) or an IR written without a baseline. A combined IR is a baseline for check --fit-only only, which asks whether a database fits an application.");
        }

        var combined = IrCombination.Of(baseline.Document, own, new IrFitOptions { StrictObjects = config.Objects == ObjectsPolicy.Strict });
        return new Succeeded<(IrDocument, EquatableArray<Diagnostic>)>((combined.Document, [.. combined.Findings.Select(f => Said(f, own, project))]));
    }

    /// <summary>A finding as a diagnostic at its procedure: an error for a change that can't be bridged, a warning otherwise.</summary>
    private static Diagnostic Said(IrCombinationFinding finding, IrDocument own, SqlProject project)
    {
        var at = $"{finding.Object}{(finding.Path.Length == 0 ? "" : " " + finding.Path)}";
        var (code, message) = finding.Kind switch
        {
            IrCombinationKind.Unbridgeable => (CliCodes.Unbridgeable, $"{at}: {finding.What}. Until then the module leaves out its function and types, since none works against both databases."),
            IrCombinationKind.PossibleRename => (CliCodes.PossibleRename, $"{at}: {finding.What}. The combined type has both keys, each optional."),
            IrCombinationKind.OnlyInCandidate or IrCombinationKind.OnlyInBaseline => (CliCodes.InOneDatabase, $"{at}: {finding.What}; the module leaves it out until the baseline moves on."),
        };

        // A procedure only the baseline has is placed nowhere: its place is in the baseline's source, not the module's.
        var obj = own.Objects.FirstOrDefault(o => o.Id == finding.Object);
        return ModuleRun.Rooted(code.At(obj is not null && SourceLocation.TryParse(obj.At, out var place) ? place : null, message), project);
    }
}
