using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

// CONTAINSTABLE and FREETEXTTABLE in FROM: the table each searches, bound as a name in FROM is. The Inference README's
// "Full-text table functions" gives the rules and the observations they rest on.
public sealed partial class Binder
{
    /// <summary>
    /// What the table a full-text call searches is: a table of this database by its name, resolved as a name in FROM is
    /// (<see cref="InSchema"/>), or why not. Its search condition, language and count of rows are a literal or a variable each
    /// (SQL Server refuses a column there), so the call reads no column of any scope.
    /// </summary>
    private SourceTarget Searched(FullTextTableReference call)
    {
        var name = call.TableName;
        var at = At(call);
        UnresolvedTarget Unresolved(BindingProblemKind kind, string detail) => new(Problem(kind, detail, at));
        if (name.ServerIdentifier is not null || name.DatabaseIdentifier is not null)
            return Unresolved(BindingProblemKind.UnsupportedSource, $"The reference {FragmentText.OneLine(name)} to another database is not modelled yet.");
        if (name.SchemaIdentifier is not null)
            return InSchema(SyntaxReader.Name(name), at);
        return SchemaNames.Resolve(Readable(), name.BaseIdentifier.Value, search) switch
        {
            OneObject one => InSchema(one.Named.Name, at),
            NoObject none => InSchema(none.InDbo, at),
            SeveralObjects several => Unresolved(
                BindingProblemKind.UnresolvedTable,
                $"{name.BaseIdentifier.Value} is not schema-qualified, and {string.Join(" and ", several.Candidates.Select(c => $"the {SchemaNames.Kind(c)} {c.Name}"))} have that name: which is searched depends on the caller's default schema."),
        };
    }

    /// <summary>
    /// Why a semantic search function (<c>SEMANTICKEYPHRASETABLE</c>, <c>SEMANTICSIMILARITYTABLE</c>,
    /// <c>SEMANTICSIMILARITYDETAILSTABLE</c>) in FROM is unknown: it needs a semantic index and a database of language statistics,
    /// which no project has been seen to use, so its columns are not modelled.
    /// </summary>
    private static string SemanticWhy(SemanticTableReference call) =>
        $"{call.SemanticFunctionType.ToString().ToUpperInvariant()} is a semantic search function, which is not modelled.";
}
