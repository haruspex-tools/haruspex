using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics.Collations;

namespace Haruspex.SqlServer.Inference;

/// <summary>Infers the IR for procedures of a model, and for query files read against it.</summary>
public static class IrInference
{
    /// <summary>Infers the IR for the given procedures of <paramref name="model"/>.</summary>
    /// <param name="model">The model.</param>
    /// <param name="procedures">The procedures to infer.</param>
    /// <param name="declarations">
    /// The results declared for procedures whose results cannot be inferred: a call of one contributes these instead of
    /// making its caller's results unknown. A procedure's own declaration is not applied to it here; that is the config
    /// layer's, which also checks it against what is inferred.
    /// </param>
    /// <param name="session">
    /// The options the callers' connections are declared to set; not known when null, so every rule that depends on one claims
    /// what holds under any value. The document records it.
    /// </param>
    /// <returns>
    /// The document, with what could not be checked, once each: a write to a table the project does not define, whose
    /// triggers (which could return result sets) are not in the project; and a note for each place the project builds JSON as
    /// text, in every module and query file. Inference always gives a document, since what it
    /// can't type is an unknown in it, so it returns a success, and says so in its type.
    /// </returns>
    /// <param name="queries">
    /// The application's query files to infer beside the procedures, each read against <paramref name="model"/>; none when null.
    /// </param>
    /// <param name="columns">
    /// The columns declared to hold JSON of a schema, part of each column's type, read wherever the column is; none when null. The
    /// document records each, and lists a claim on each object whose results may rest on one.
    /// </param>
    /// <exception cref="ArgumentException">A name is not a procedure of the model: a fault of the caller, which selected it.</exception>
    public static Succeeded<IrDocument> Infer(
        DatabaseModel model,
        IReadOnlyList<ObjectName> procedures,
        IReadOnlyDictionary<ObjectName, DeclaredResults>? declarations = null,
        SessionOptions? session = null,
        IReadOnlyList<QueryFile>? queries = null,
        IReadOnlyList<DeclaredColumn>? columns = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(procedures);
        queries ??= [];
        declarations ??= new Dictionary<ObjectName, DeclaredResults>();
        session ??= SessionOptions.NotKnown;
        columns ??= [];
        var warnings = new List<Diagnostic>();
        var summaries = new Summaries(model, declarations, warnings, session, columns);

        // UNMASK declared, but a module runs as another user, whose permissions decide what it reads: every mask is kept.
        if (session.MasksAreLifted && summaries.RunsAsAnother is { } another)
        {
            warnings.Add(InferenceCodes.UnmaskNotApplied.At(
                another.Span.Start,
                $"The session declares that the callers hold UNMASK, but {another.Name} runs as another user, by WITH EXECUTE AS or an EXECUTE AS statement, whose permissions decide what it and whatever it calls read, so masked columns are still read as masked everywhere."));
        }
        var analysed = procedures
            .Distinct()
            .Select(name => model.FindProcedure(name)
                ?? throw new ArgumentException($"No procedure named {name} in the project.", nameof(procedures)))
            .Select(procedure => (procedure, summaries.Analyse(procedure)))
            .ToList();
        var files = new QueryFiles(model, summaries);
        var queried = queries.Select(query => (query, files.Analyse(query))).ToList();
        var document = new IrBuilder(summaries.Nodes, model.Descriptions).Build(analysed, queried, session);

        // A caller's results that include a callee's declared results rest on that declaration, so the claim is listed.
        var claims = analysed.Select(a => (Object: a.procedure.Name.ToString(), Analysis: a.Item2))
            .Concat(queried.Select(q => (Object: q.query.Path, Analysis: q.Item2)))
            .Where(a => a.Analysis.Results is not null)
            .SelectMany(a => a.Analysis.Declared.Select(callee => (a.Object, Callee: callee)))
            .Select(c => new IrOverride(c.Object, "results", IrOverrideSetting.Results, declarations[c.Callee].Why) { Via = c.Callee.ToString() })
            .ToList();

        // Results that may hold a declared column's JSON rest on its declaration: listed for each object whose body, or a body
        // it reaches, reads the column. Results that can't be listed claim nothing to rest on.
        claims.AddRange(analysed.Select(a => (Object: a.procedure.Name.ToString(), a.procedure.Span, Analysis: a.Item2))
            .Concat(queried.Select(q => (Object: q.query.Path, q.query.Span, Analysis: q.Item2)))
            .Where(a => a.Analysis.Results is not null)
            .SelectMany(a => summaries.DeclaredColumns.ReadFrom(a.Span).Select(column => new IrOverride(a.Object, "results", IrOverrideSetting.ColumnJson, null) { Via = column })));
        var folds = IrDocumentFolds.Named(document).ToEquatableMap(name => name, name => SqlTypes.Fold(CollationFold.Named(name)));

        // JSON built as text, wherever the project writes it, whether or not anything reads it as JSON
        // (docs/decisions/0085-json-built-as-text-is-a-note-wherever-it-is-written.md).
        warnings.AddRange(JsonTextNotes.In(model, queries, summaries.JsonText));

        // A result set returned as FOR XML, which FOR JSON would build typed (docs/decisions/0133-an-xml-value-is-a-kind-of-its-own-a-branded-string.md).
        warnings.AddRange(XmlResultNotes.In(model, queries));
        return new Succeeded<IrDocument>(document with { Overrides = [.. claims], Folds = folds, Columns = [.. columns.Select(c => c.Record)] }, [.. warnings]);
    }

    /// <summary>
    /// The table columns a view's column reads, as <c>schema.table.column</c>: what a declaration of the view's column should name
    /// instead, since a view's column holds what it reads. Empty when the view or the column isn't there, or reads no column.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="view">The view.</param>
    /// <param name="column">The view's column, by name, ignoring case.</param>
    public static IReadOnlyList<string> ViewColumnSources(DatabaseModel model, ObjectName view, string column)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(column);
        if (model.FindReadable(view) is not View found)
            return [];
        var read = new Summaries(model, new Dictionary<ObjectName, DeclaredResults>(), [], SessionOptions.NotKnown, []).View(found, uncommitted: false, found.Span);
        var index = read.Names?.ToList().FindIndex(n => n.Equals(column, StringComparison.OrdinalIgnoreCase)) ?? -1;
        return index >= 0 && read.From is { } from && index < from.Count ? [.. from[index].Select(c => c.Column).Distinct(StringComparer.Ordinal)] : [];
    }
}
