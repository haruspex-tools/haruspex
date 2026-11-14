using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Inference;

/// <summary>
/// A procedure whose text the source doesn't hold (<see cref="EncryptedBody"/>), as a DACPAC extracted from a database holds an
/// encrypted one: its results are unknown with a reason of their own, never the "no results" its placeholder would claim.
/// </summary>
public partial class ExecTests
{
    /// <summary>The files read as a folder, with the procedure <paramref name="encrypted"/>'s body replaced as an extract's is.</summary>
    private static IrDocument InferWithEncrypted(string encrypted, params (string Name, string Sql)[] procedures)
    {
        using var dir = new TempDirectory();
        dir.File("tables.sql", Tables);
        foreach (var (name, sql) in procedures)
            dir.File($"{name}.sql", sql);
        var read = SqlScriptFolder.Read(dir.Path);
        Assert.Empty(read.Diagnostics);
        var model = read.Model;
        var hidden = new DatabaseModel(
            model.Tables,
            model.UserTypes,
            model.Procedures.Select(p => p.Name.Name == encrypted ? p with { Body = new EncryptedBody() } : p),
            model.Functions,
            model.Views,
            model.Synonyms,
            model.Triggers,
            model.CascadingKeys,
            model.TableRedefinitions);
        return IrInference.Infer(hidden, [.. hidden.Procedures.Select(p => p.Name)]).Value;
    }

    [Fact]
    public void An_encrypted_procedure_whose_text_the_source_lacks_has_unknown_results_with_their_own_reason()
    {
        var why = ResultsUnknown(InferWithEncrypted("p", ("p", "CREATE PROCEDURE dbo.p AS SELECT t.id FROM dbo.t AS t;")));

        Assert.Equal(IrUnknownReason.EncryptedDefinition, why.Reason);
        Assert.Contains("encrypted", why.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_call_of_an_encrypted_procedure_whose_text_the_source_lacks_leaves_the_caller_s_results_unknown()
    {
        var doc = InferWithEncrypted(
            "q",
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p AS EXEC dbo.q;"));

        Assert.Equal(IrUnknownReason.EncryptedDefinition, ResultsUnknown(doc).Reason);
    }
}
