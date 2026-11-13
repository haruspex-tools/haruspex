using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.Tests.Semantics;

/// <summary>The words a redefinition's reason gives its statement, which reach the IR's details: the statement as T-SQL writes it.</summary>
public class RedefinitionsTests
{
    private static readonly Dictionary<RedefiningStatement, string> Words = new()
    {
        [RedefiningStatement.AlterTable] = "ALTER TABLE",
        [RedefiningStatement.DropTable] = "DROP TABLE",
        [RedefiningStatement.AlterProcedure] = "ALTER PROCEDURE",
        [RedefiningStatement.DropProcedure] = "DROP PROCEDURE",
        [RedefiningStatement.AlterFunction] = "ALTER FUNCTION",
        [RedefiningStatement.DropFunction] = "DROP FUNCTION",
        [RedefiningStatement.CreateView] = "CREATE VIEW",
        [RedefiningStatement.AlterView] = "ALTER VIEW",
        [RedefiningStatement.CreateOrAlterView] = "CREATE OR ALTER VIEW",
    };

    public static TheoryData<RedefiningStatement> Statements { get; } = [.. Enum.GetValues<RedefiningStatement>()];

    [Theory]
    [MemberData(nameof(Statements))]
    public void Each_statement_is_named_as_t_sql_writes_it(RedefiningStatement statement) =>
        Assert.Equal(Words[statement], Redefinitions.Statement(statement));
}
