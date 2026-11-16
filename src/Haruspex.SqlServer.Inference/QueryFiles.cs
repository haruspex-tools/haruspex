using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Infers a query file as a procedure's body is inferred, once its parameters have types: the variables it uses without
/// declaring, each typed by its declaration comment, or else by what its uses decide (<see cref="ParameterUses"/>), or else
/// unknown. README.md in this folder ("Query files") says how, and
/// <c>docs/decisions/0059-a-query-file-s-parameters-are-typed-from-their-uses.md</c> why.
/// </summary>
/// <remarks>
/// Two walks: the first with every undeclared variable undeclared, so that the uses are read where the server reads them, the
/// second with the parameters typed, which is the walk of what runs. A parameter accepts NULL unless the body proves it can't
/// be, as a procedure's does.
/// </remarks>
/// <param name="model">The model the file is read against.</param>
/// <param name="summaries">What the file's body may ask of the objects it reads or calls.</param>
internal sealed class QueryFiles(DatabaseModel model, Summaries summaries)
{
    /// <summary>The file's parameters, in the order its text first uses them, and its results.</summary>
    public ProcedureAnalysis Analyse(QueryFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var (parameters, undecided) = Parameters(file);
        var shapes = parameters.Select(p => new ParameterShape(
            p.Name,
            undecided.TryGetValue(new LocalName(p.Name), out var why) ? why : SqlTypes.ToParameterShape(model, p.Type, p.At),
            HasDefault: false,
            IsOutput: false)).ToList();

        var walked = new Walker(model, file.Span, summaries, context: CallContext.Top).Walk(parameters, file.Statements, undecided: undecided);
        if (walked.Blocker is { } blocker)
            return new ProcedureAnalysis([.. shapes], null, blocker, walked.Declared);
        if (walked.End is null)
            return new ProcedureAnalysis([.. shapes], [], null, walked.Declared);
        var (results, varying) = Walker.Results(walked.End.Results);
        return new ProcedureAnalysis([.. shapes], results is null ? null : [.. results], varying, walked.Declared);
    }

    /// <summary>Each parameter as the call declares it, and why each one nothing decides is unknown.</summary>
    private (List<Parameter> Parameters, Dictionary<LocalName, UnknownShape> Undecided) Parameters(QueryFile file)
    {
        var uses = Uses(file);
        var parameters = new List<Parameter>();
        var undecided = new Dictionary<LocalName, UnknownShape>();
        foreach (var first in BatchVariables.Undeclared(file.Statements))
        {
            var name = new LocalName(first.Name);
            var at = file.Span.At(first.First);
            if (file.Declared.FirstOrDefault(p => new LocalName(p.Name) == name) is { } declared)
            {
                // The declaration's name, though spelt as the text first uses it, so that a key the caller passes has one spelling.
                parameters.Add(declared with { Name = first.Name, At = at });
                continue;
            }

            var types = uses.Where(u => u.Name == name).ToList();
            var distinct = types.Select(u => u.Type).Distinct().ToList();
            if (distinct is [var type])
            {
                parameters.Add(new Parameter(first.Name, type.Declaration, IsOutput: false, Default: null, at));
                continue;
            }

            parameters.Add(new Parameter(first.Name, null, IsOutput: false, Default: null, at));
            undecided[name] = new UnknownShape(IrUnknownReason.UndecidedParameter, Undecided(first.Name, types), at);
        }

        return (parameters, undecided);
    }

    /// <summary>
    /// What the uses of the file's undeclared variables say of their types, read in a walk where they are undeclared. Its
    /// declared parameters are declared there, so that what they hold types what they are used with.
    /// </summary>
    private IReadOnlyList<ParameterUses.ParameterUse> Uses(QueryFile file)
    {
        // The walk reads a copy of the statement list: what a body declares is bound once for each list (Bindings), and the walk
        // of what runs declares more of it.
        var copy = new StatementList { ScriptTokenStream = file.Statements.ScriptTokenStream, FirstTokenIndex = file.Statements.FirstTokenIndex, LastTokenIndex = file.Statements.LastTokenIndex };
        foreach (var statement in file.Statements.Statements)
            copy.Statements.Add(statement);

        var uses = new ParameterUses(model, file.Span, summaries.Bindings.For(file.Span));
        new Walker(model, file.Span, summaries, context: CallContext.Top, uses: uses).Walk(file.Declared, copy);
        return uses.Found;
    }

    /// <summary>Why a parameter is unknown: no use decides it, or its uses decide it differently, each named with where.</summary>
    private static string Undecided(string name, List<ParameterUses.ParameterUse> uses)
    {
        var declare = $"Declare it in the file: -- @param {name} <type>.";
        if (uses.Count == 0)
        {
            return $"Nothing decides the type of {name}: it is never compared with a column or a literal, in arithmetic with one, a count of rows, " +
                $"assigned to a column, or passed to a procedure or function. {declare}";
        }

        var each = uses.GroupBy(u => u.Type).Select(g => $"{g.Key} where it is {g.First().How} ({g.First().At})");
        return $"The uses of {name} decide different types: {string.Join("; ", each)}. {declare}";
    }
}
