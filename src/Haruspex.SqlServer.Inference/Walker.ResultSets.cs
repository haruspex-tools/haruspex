using System.Collections.Immutable;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Inference.Typing;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// EXEC … WITH RESULT SETS: the declaration types the call, whatever its callee, since the server enforces it. README.md in this
// folder ("Result sets a call declares") gives the model, and ExecObservationTests.ResultSets.cs in the differential tests the
// observations it rests on.
internal sealed partial class Walker
{
    /// <summary>
    /// The result sets a call's <c>WITH RESULT SETS</c> declares, as one path returning each in turn: none for <c>NONE</c>; null
    /// without the clause or with <c>UNDEFINED</c>, which is as none (observed). A call that succeeds returned exactly these, each
    /// present: any other number fails it (11535, 11536), and so does a set of another width (11537), a value that doesn't
    /// convert (8114, 11538) and a NULL in a <c>NOT NULL</c> column (11553).
    /// </summary>
    private ResultTrack? DeclaredSets(ExecuteStatement execute)
    {
        if (execute.Options.OfType<ResultSetsExecuteOption>().FirstOrDefault(o => o.ResultSetsOptionKind != ResultSetsOptionKind.Undefined) is not { } option)
            return null;
        var track = ResultTrack.None;
        var at = At(execute);
        if (option.ResultSetsOptionKind == ResultSetsOptionKind.ResultSetsDefined)
        {
            for (var i = 0; i < option.Definitions.Count; i++)
                track = track.Append(typer.DeclaredSet(option.Definitions[i], i), at);
        }

        return track;
    }

    /// <summary>
    /// What a call whose result sets are declared gave, when its callee was walked: the declared sets on every path the callee
    /// returns from, unless its results never match them (<see cref="NeverMatches"/>). An error the call raises, the callee's own
    /// or the declaration's, leaves after any number of the declared sets, so each path the callee leaves or returns from
    /// reaches a CATCH that way.
    /// </summary>
    private Called Declared(Walked walked, ResultTrack declared, FlowState state, ImmutableHashSet<LocalName> own, bool inserting, bool needEscapes, string call, string callee, SourceLocation at)
    {
        var end = walked.End;
        if (needEscapes)
        {
            var sent = state.Results.Then(declared.Prefixes());
            Escaped([.. walked.Escapes.Concat(end is null ? [] : [end]).Select(e =>
            {
                var back = Returned(state, e, own, at);
                return inserting ? back : back.WithResults(sent);
            })]);
        }

        if (end is null || NeverMatches(end.Results, declared, call, callee, at))
            return new Called(null, ResultTrack.None, null);
        return new Called(Returned(state, end, own, at), declared, null);
    }

    /// <summary>
    /// Whether the callee's results never match the declared ones, so every call fails, warned of
    /// (<see cref="InferenceCodes.DeclaredSetsNeverMatch"/>): no path of it returns as many sets as declared, or at some position
    /// every set it may return there is of another width. Each declared column narrower than the callee's is warned of on the way
    /// (<see cref="InferenceCodes.NarrowerDeclaredColumn"/>).
    /// </summary>
    private bool NeverMatches(ResultTrack returned, ResultTrack declared, string call, string callee, SourceLocation at)
    {
        var count = declared.Counts.Single();
        if (!returned.Counts.Contains(count))
        {
            summaries.Warn(InferenceCodes.DeclaredSetsNeverMatch.At(
                at,
                $"{call} WITH RESULT SETS declares {Sets(count)}, but {callee} returns {string.Join(" or ", returned.Counts)}, so every call fails ({(returned.Counts.Max > count ? 11535 : 11536)}). Declare the sets it returns."));
            return true;
        }

        for (var i = 0; i < count && i < returned.Positions.Count; i++)
        {
            var columns = Fields(declared.Positions[i].Single().Result.Shape);
            if (columns is null)
                continue;
            var widths = returned.Positions[i].Select(p => Fields(p.Result.Shape)?.Count ?? (p.Result.Encoding == Ir.IrEncoding.Json ? 1 : null)).ToList();
            if (widths.Count > 0 && widths.All(w => w is { } n && n != columns.Count))
            {
                summaries.Warn(InferenceCodes.DeclaredSetsNeverMatch.At(
                    at,
                    $"{call} WITH RESULT SETS declares result set {i + 1} of {Columns(columns.Count)}, but {callee} returns it of {string.Join(" or ", widths.Distinct().Order())}, so every call fails (11537). Declare the columns it returns."));
                return true;
            }

            foreach (var produced in returned.Positions[i])
            {
                if (Fields(produced.Result.Shape) is { } fields && fields.Count == columns.Count)
                {
                    for (var j = 0; j < fields.Count; j++)
                        Narrowed(columns[j], fields[j], i, j, call, callee, at);
                }
            }
        }

        return false;

        static string Sets(int n) => n == 1 ? "1 result set" : $"{n} result sets";
        static string Columns(int n) => n == 1 ? "1 column" : $"{n} columns";
    }

    /// <summary>The fields of each row of a rowset, of one shape; null for any other.</summary>
    private static Collections.EquatableArray<FieldShape>? Fields(Shape shape) => shape is ArrayShape { Items: ObjectShape row } ? row.Fields : null;

    /// <summary>
    /// Warns of a declared column that doesn't hold every value of the callee's it converts
    /// (<see cref="InferenceCodes.NarrowerDeclaredColumn"/>): a string or binary value cut short; a number that may not fit, so it
    /// is written <c>*</c> in a string (observed) or fails the call; a type the callee's never converts to (11538); and
    /// <c>NOT NULL</c> over a column that may be null (11553). A date or time rounded to fewer fractional digits, a decimal to
    /// fewer places, and national text into a code page that lacks some of its characters are not warned of: each value still
    /// arrives, close to the callee's.
    /// </summary>
    private void Narrowed(FieldShape declared, FieldShape returned, int set, int column, string call, string callee, SourceLocation at)
    {
        var to = (declared.Type as ScalarShape)?.BuiltIn;
        var from = returned.Type switch
        {
            ScalarShape { BuiltIn: var type } => type,
            JsonTextShape => new ScalarType(BuiltInType.NVarChar, -1),
            _ => null,
        };

        string? why = null;
        if (to is not null && from is not null)
        {
            if (ConversionChart.Of(from.Type, to.Type) == ConversionKind.Refused)
                why = $"{from.Text} never converts to it, so every call that returns a row there fails (11538)";
            else if ((from.IsString || from.IsBinary) && (to.IsString || to.IsBinary))
                why = !to.IsMax && (from.IsMax || to.Length < from.Length) ? $"a value of {from.Text} is cut short" : null;
            else if (!Conversions.Converts(Operand.OfType(from), to))
                why = $"a value of {from.Text} may not fit, so {(from.IsInteger && to.IsString ? "it is written *" : "the call fails (8114)")}";
        }

        if (why is null && declared.NullableBecause.Count == 0 && returned.NullableBecause.Count > 0)
            why = "it is declared NOT NULL, but the callee's may be null, which fails the call (11553)";
        if (why is null)
            return;

        summaries.Warn(InferenceCodes.NarrowerDeclaredColumn.At(
            at,
            $"{call} WITH RESULT SETS declares {declared.Name} {(declared.Type as ScalarShape)?.Type.Sql}, column {column + 1} of result set {set + 1}, over {callee}'s {returned.Name}: {why}. Declare a type that holds every value, or convert it in {callee}."));
    }
}
