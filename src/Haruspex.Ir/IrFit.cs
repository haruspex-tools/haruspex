using System.Text.RegularExpressions;
using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>
/// Whether one document's values fit within another's: what an application built against one document (its schemas and
/// functions) does when it meets a database described by another. <c>haruspex check --baseline</c> asks it with the baseline as
/// the application and the database being checked as the database: the application already running meeting the database
/// being deployed. Asked the other way round, it is the new application meeting the old database.
/// </summary>
/// <remarks>
/// <para>
/// The two directions of flow are judged apart. <b>Results</b> flow from the database to the application, so they fit when
/// every value the database can return is one the application's schemas accept: a value proved never null may not become
/// nullable, a length, a range, a value set or a number of rows may narrow but not widen, a key or a result set may not go. A
/// key or a trailing result set the application doesn't know is set aside by default, and refused under
/// <see cref="IrFitOptions.StrictObjects"/>, as the generated code does (decisions 0008 and 0063). <b>Parameters</b> flow the
/// other way, so they fit when everything the application may send is something the database's procedure takes: a parameter it
/// doesn't send must have a default, one it may send must still be there, one it may leave out must keep its default, and a
/// type may widen but not narrow. A procedure the application calls must still be there; one it doesn't know is no concern of
/// it.
/// </para>
/// <para>
/// The claims compared are the IR's, as <see cref="IrComparison"/> compares them, whatever an emitter makes of each: a claim
/// one target can't check is still one the application, or another emitter, relies on. Where a claim can't be shown to fit
/// (a value that became unknown, a value set compared under another fold), it doesn't: fit is a soundness claim, so it errs
/// towards refusing. Why each rule is as it is: docs/decisions/0068-a-baseline-fits-when-the-running-application-accepts-every-value.md.
/// </para>
/// <para>
/// Objects are matched by id, parameters and fields by name, result sets by position; a reference is followed into the type
/// it names on each side, and each pair of names once per object, so a recursive type ends the walk. Findings are in a stable
/// order: each object's by id, each in the order its walk meets them.
/// </para>
/// </remarks>
public static partial class IrFit
{
    /// <summary>
    /// Every difference that matters to an application built against <paramref name="application"/> meeting a database
    /// described by <paramref name="database"/>, each refused or ignored; empty when everything fits and nothing is set aside.
    /// </summary>
    public static EquatableArray<IrFitFinding> Of(IrDocument application, IrDocument database, IrFitOptions options)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(database);

        var findings = new List<IrFitFinding>();
        var met = database.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        foreach (var known in application.Objects.OrderBy(o => o.Id, StringComparer.Ordinal))
        {
            if (met.TryGetValue(known.Id, out var now))
                new ObjectWalk(application, database, known.Id, options, findings).Object(known, now);
            else
                findings.Add(new IrFitFinding(new IrChange(IrChangeKind.ObjectRemoved, known.Id, ""), IrFitVerdict.Refused, "the application calls it"));
        }

        return [.. findings];
    }

    /// <summary>
    /// The walk over one object both documents have: its parameters, then its results. It holds the findings and the pairs of
    /// type names it has followed, which is what ends the walk at a recursive type; both live only as long as the walk.
    /// </summary>
    private sealed partial class ObjectWalk(IrDocument application, IrDocument database, string id, IrFitOptions options, List<IrFitFinding> findings)
    {
        /// <summary>The document the application was built against.</summary>
        public IrDocument Application { get; } = application;

        /// <summary>The document of the database it meets.</summary>
        public IrDocument Database { get; } = database;

        /// <summary>How the application reacts to what it doesn't know.</summary>
        public IrFitOptions Options { get; } = options;

        public void Object(IrObject known, IrObject met)
        {
            if (known.Kind != met.Kind)
            {
                Add(findings, IrChangeKind.ObjectKindChanged, [], IrFitVerdict.Refused, $"the application calls it as a {IrWireNames.Of(known.Kind)}", IrWireNames.Of(known.Kind), IrWireNames.Of(met.Kind));
                return;
            }

            Parameters(known.Parameters, met.Parameters);
            Results(known.Results, met.Results);
        }

        /// <summary>
        /// The parameters, which the application sends: matched by name, since the functions bind each by its name, so an order
        /// that changed doesn't matter. Each type is walked with the procedure's as the one that accepts, and the application's
        /// as what is sent.
        /// </summary>
        private void Parameters(EquatableArray<IrParameter> known, EquatableArray<IrParameter> met)
        {
            IrPathSegment[] parameters = [new IrPathKey("parameters")];
            var taken = met.ToDictionary(p => p.Name, StringComparer.Ordinal);
            var sent = known.ToDictionary(p => p.Name, StringComparer.Ordinal);
            foreach (var parameter in known.Where(p => !taken.ContainsKey(p.Name)))
            {
                Add(findings, IrChangeKind.ParameterRemoved, [.. parameters, new IrPathKey(parameter.Name)], IrFitVerdict.Refused,
                    "the application may send it, and the procedure no longer takes it", before: IrClaimText.Value(parameter.Type, nullable: false));
            }

            // SQL Server refuses a call that leaves out a parameter without a default (error 201), and fills in one with.
            foreach (var parameter in met.Where(p => !sent.ContainsKey(p.Name)))
            {
                Add(findings, IrChangeKind.ParameterAdded, [.. parameters, new IrPathKey(parameter.Name)],
                    parameter.HasDefault ? IrFitVerdict.Ignored : IrFitVerdict.Refused,
                    parameter.HasDefault ? "the application doesn't send it, and it has a default" : "the application doesn't send it, and it has no default",
                    after: IrClaimText.Value(parameter.Type, nullable: false));
            }

            foreach (var parameter in known.Where(p => taken.ContainsKey(p.Name)))
            {
                var now = taken[parameter.Name];
                IrPathSegment[] path = [.. parameters, new IrPathKey(parameter.Name)];
                if (parameter.HasDefault && !now.HasDefault)
                    Add(findings, IrChangeKind.ParameterDefaultChanged, path, IrFitVerdict.Refused, "the application may leave it out", "true", "false");

                // Sent as OUTPUT to a parameter not declared so, a call fails (error 8162); the reverse is a plain argument.
                if (parameter.IsOutput && !now.IsOutput)
                    Add(findings, IrChangeKind.ParameterDirectionChanged, path, IrFitVerdict.Refused, "the application sends it as an output parameter", "true", "false");

                if (parameter.Type is IrUnknownType && now.Type is IrUnknownType)
                    BothUnknown(path, $"parameters.{parameter.Name}", parameter.Type, now.Type);
                else
                    new Values(this, Flow.Parameters, findings).Type(path, now.Type, parameter.Type);
            }
        }

        /// <summary>
        /// A parameter whose type neither document models. It fits only when both name the same built-in type whose name is all
        /// there is to it (<see cref="WholeByName"/>): whatever the application sent before, the procedure still takes. Any other
        /// type may have changed under its name, which the IR can't show.
        /// </summary>
        /// <remarks>
        /// The IR names such a type only in the detail of its unknown, as <c>SqlTypes</c> in inference writes it ("The type
        /// hierarchyid is not modelled yet."), with a user's type qualified by its schema. Observed: a table type that gains a
        /// column fails the old call (error 500), and an <c>xml</c> schema collection that narrows an element fails it (6926), each
        /// under the same name; and the IR spells <c>xml</c> alike with a collection and without.
        /// </remarks>
        private void BothUnknown(IrPathSegment[] path, string at, IrType known, IrType met)
        {
            const string Why = "the procedure's type of it isn't modelled, so it can't be shown to take what the application may send";
            switch (Named(Application, at), Named(Database, at))
            {
                // A built-in type's name is the same in any case, as SQL Server reads it; a user's type is named as written.
                case ({ } was, { } now) when WholeByName(was) && string.Equals(was, now, StringComparison.OrdinalIgnoreCase):
                    return;
                case ({ } was, { } now) when was == now:
                    Add(findings, IrChangeKind.TypeUnproven, path, IrFitVerdict.Refused,
                        "the IR doesn't hold its definition (an xml schema collection, a user type's columns or class), which may have changed under its name", was, now);
                    return;
                case ({ } was, { } now):
                    Add(findings, IrChangeKind.SqlTypeChanged, path, IrFitVerdict.Refused, Why, was, now);
                    return;
                default:
                    Add(findings, IrChangeKind.TypeChanged, path, IrFitVerdict.Refused, Why, IrClaimText.Type(known), IrClaimText.Type(met));
                    return;
            }
        }

        /// <summary>The SQL type the unknown at <paramref name="at"/> names, when it is the one unknown there and is a type not modelled.</summary>
        private string? Named(IrDocument document, string at) =>
            document.Unknowns.Where(u => u.Object == id && u.Path == at).ToList() is [{ Reason: IrUnknownReason.UnsupportedSqlType } unknown]
                && NotModelled().Match(unknown.Detail) is { Success: true } match
                ? match.Groups["type"].Value
                : null;

        /// <summary>
        /// The result sets, which the database returns: matched by position, as the functions read them. Results the application
        /// couldn't list are handed over as they arrive, whatever they are, so anything fits them.
        /// </summary>
        /// <remarks>
        /// The application's function counts the sets every call returns, and fails a call that returns fewer
        /// (docs/decisions/0063-a-function-ignores-result-sets-after-those-it-knows.md): those before the first that may be missing,
        /// where a <c>FOR JSON</c> set absent only because no rows matched still arrives, and so counts. Each set it counts must be
        /// one the database still sends on every call. The count is the one <c>MssqlFunction.SetsAlwaysReturned</c> makes.
        /// </remarks>
        private void Results(EquatableArray<IrResult>? known, EquatableArray<IrResult>? met)
        {
            IrPathSegment[] results = [new IrPathKey("results")];
            switch (known, met)
            {
                case (null, _):
                    return;
                case (not null, null):
                    Add(findings, IrChangeKind.ResultsBecameUnknown, results, IrFitVerdict.Refused, "the application's schemas expect the result sets it knew");
                    return;
                case ({ } before, { } after):
                    var counted = before.TakeWhile(AlwaysSent).Count();
                    for (var i = 0; i < Math.Max(before.Count, after.Count); i++)
                    {
                        IrPathSegment[] path = [.. results, new IrPathIndex(i)];
                        if (i >= after.Count)
                        {
                            Add(findings, IrChangeKind.ResultRemoved, path, IrFitVerdict.Refused, "the application expects it");
                        }
                        else if (i >= before.Count)
                        {
                            Add(findings, IrChangeKind.ResultAdded, path, Options.StrictObjects ? IrFitVerdict.Refused : IrFitVerdict.Ignored, Options.StrictObjects
                                ? "the application, with \"objects\": \"strict\", refuses a result set after those it knows"
                                : "the application ignores a result set after those it knows");
                        }
                        else
                        {
                            Result(path, before[i], after[i], i < counted);
                        }
                    }

                    return;
            }
        }

        /// <summary>Whether a set arrives on every call: it is never absent, or absent only for want of rows, as a <c>FOR JSON</c> set then still arrives.</summary>
        private static bool AlwaysSent(IrResult result) => result.AbsentBecause.All(reason => reason.Rule == IrNullRule.NoRows);

        private void Result(IrPathSegment[] path, IrResult known, IrResult met, bool counted)
        {
            if (known.Encoding != met.Encoding)
            {
                Add(findings, IrChangeKind.EncodingChanged, path, IrFitVerdict.Refused, "the application reads it as it was", IrWireNames.Of(known.Encoding), IrWireNames.Of(met.Encoding));
                return;
            }

            if (met.MayBeAbsent && !known.MayBeAbsent)
                Add(findings, IrChangeKind.BecameMayBeAbsent, path, IrFitVerdict.Refused, "the application's schema requires it");
            else if (counted && !AlwaysSent(met))
                Add(findings, IrChangeKind.BecameMayBeAbsent, path, IrFitVerdict.Refused, "the application's function counts on it arriving on every call");

            // How many rows a result has is its array's bounds and its cardinality together, which is how the schemas bound it.
            new Values(this, Flow.Results, findings).Type(path, Bounded(known), Bounded(met));

            static IrType Bounded(IrResult result) => result.Type is IrArray array ? array with { MinItems = result.FewestItems, MaxItems = result.MostItems } : result.Type;
        }

        /// <summary>A finding at <paramref name="path"/> in this object, the application's claim before and the database's after.</summary>
        private void Add(List<IrFitFinding> to, IrChangeKind kind, IrPathSegment[] path, IrFitVerdict verdict, string why, string? before = null, string? after = null) =>
            to.Add(new IrFitFinding(new IrChange(kind, id, IrPath.Render(path), before, after), verdict, why));
    }

    /// <summary>
    /// Whether a type the IR doesn't model is defined by its name alone: a built-in type, with a binary type's length, rather
    /// than one the database defines (a table type, a CLR type, an alias) or <c>xml</c>, which may name a schema collection the
    /// IR doesn't spell.
    /// </summary>
    private static bool WholeByName(string type) =>
        type.ToUpperInvariant() is "HIERARCHYID" or "GEOGRAPHY" or "GEOMETRY" or "SQL_VARIANT" || BinaryType().IsMatch(type);

    [GeneratedRegex(@"^The type (?<type>.+) is not modelled yet\.$", RegexOptions.CultureInvariant)]
    private static partial Regex NotModelled();

    [GeneratedRegex(@"^(var)?binary\((\d+|max)\)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BinaryType();

    /// <summary>Which way values flow, which decides which document's type accepts and which gives.</summary>
    private enum Flow
    {
        /// <summary>From the database to the application: the application's type accepts what the database's gives.</summary>
        Results,

        /// <summary>From the application to the database: the procedure's type accepts what the application's gives.</summary>
        Parameters,
    }
}
